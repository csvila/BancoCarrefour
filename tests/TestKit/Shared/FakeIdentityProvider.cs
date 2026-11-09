using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;

namespace BancoCarrefour.TestKit;

/// <summary>Comportamento do IdP fake para testes de fail-closed (TC-015/TC-017).</summary>
public enum FakeIdpMode
{
    Healthy,
    ServiceUnavailable,
    ConnectionRefused,
    Timeout,
    MalformedJwks,
    SymmetricKeyOnlyJwks,
}

/// <summary>
/// IdP OIDC fake servido in-process por um <see cref="HttpMessageHandler"/> plugado em
/// <c>JwtBearerOptions.BackchannelHttpHandler</c>. O handler JwtBearer real continua buscando discovery e JWKS
/// pelo <c>ConfigurationManager</c> padrão; nenhuma regra de validação é alterada (ver H-02 / relatório).
/// Todas as chaves são geradas em runtime e nunca são persistidas.
/// </summary>
public sealed class FakeIdentityProvider : IDisposable
{
    public const string Authority = "https://idp.test.invalid/";
    public const string Issuer = "https://idp.test.invalid/";
    public const string JwksUri = "https://idp.test.invalid/jwks";
    public const string DiscoveryPath = "/.well-known/openid-configuration";

    public const string RsaKid = "rsa-1";
    public const string Ec256Kid = "ec256-1";
    public const string Ec384Kid = "ec384-1";
    public const string SymmetricKid = "oct-1";

    private readonly Lock _gate = new();
    private readonly List<JsonObject> _publishedKeys = [];
    private readonly List<string> _unexpectedRequests = [];
    private int _discoveryRequests;
    private int _jwksRequests;

    public FakeIdentityProvider()
    {
        Rsa = RSA.Create(2048);
        Ec256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Ec384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        SymmetricSecret = RandomNumberGenerator.GetBytes(64);

        PublishRsaKey(RsaKid, Rsa);
        PublishEcKey(Ec256Kid, Ec256, "P-256");
        PublishEcKey(Ec384Kid, Ec384, "P-384");
    }

    public RSA Rsa { get; }

    public ECDsa Ec256 { get; }

    public ECDsa Ec384 { get; }

    /// <summary>Segredo usado apenas no modo <see cref="FakeIdpMode.SymmetricKeyOnlyJwks"/>.</summary>
    public byte[] SymmetricSecret { get; }

    public FakeIdpMode Mode { get; set; } = FakeIdpMode.Healthy;

    public int DiscoveryRequests => Volatile.Read(ref _discoveryRequests);

    public int JwksRequests => Volatile.Read(ref _jwksRequests);

    public IReadOnlyList<string> UnexpectedRequests
    {
        get
        {
            lock (_gate)
            {
                return [.. _unexpectedRequests];
            }
        }
    }

    public void PublishRsaKey(string kid, RSA rsa)
    {
        ArgumentNullException.ThrowIfNull(rsa);
        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        var jwk = new JsonObject
        {
            ["kty"] = "RSA",
            ["use"] = "sig",
            ["kid"] = kid,
            ["n"] = Base64UrlEncoder.Encode(parameters.Modulus!),
            ["e"] = Base64UrlEncoder.Encode(parameters.Exponent!),
        };
        lock (_gate)
        {
            _publishedKeys.Add(jwk);
        }
    }

    public void PublishEcKey(string kid, ECDsa ecdsa, string curve)
    {
        ArgumentNullException.ThrowIfNull(ecdsa);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: false);
        var jwk = new JsonObject
        {
            ["kty"] = "EC",
            ["use"] = "sig",
            ["kid"] = kid,
            ["crv"] = curve,
            ["x"] = Base64UrlEncoder.Encode(parameters.Q.X!),
            ["y"] = Base64UrlEncoder.Encode(parameters.Q.Y!),
        };
        lock (_gate)
        {
            _publishedKeys.Add(jwk);
        }
    }

    public void RemoveKey(string kid)
    {
        lock (_gate)
        {
            _publishedKeys.RemoveAll(k => (string?)k["kid"] == kid);
        }
    }

    public HttpMessageHandler CreateHandler() => new BackchannelHandler(this);

    public void Dispose()
    {
        Rsa.Dispose();
        Ec256.Dispose();
        Ec384.Dispose();
    }

    private static string BuildDiscoveryDocument() =>
        new JsonObject
        {
            ["issuer"] = Issuer,
            ["jwks_uri"] = JwksUri,
            ["id_token_signing_alg_values_supported"] = new JsonArray("RS256", "PS256", "ES256"),
        }.ToJsonString();

    private string BuildJwks()
    {
        switch (Mode)
        {
            case FakeIdpMode.MalformedJwks:
                return "{ \"keys\": [ { \"kty\": \"RSA\", \"n\": ";
            case FakeIdpMode.SymmetricKeyOnlyJwks:
                var oct = new JsonObject
                {
                    ["kty"] = "oct",
                    ["use"] = "sig",
                    ["kid"] = SymmetricKid,
                    ["k"] = Base64UrlEncoder.Encode(SymmetricSecret),
                };
                return new JsonObject { ["keys"] = new JsonArray(oct) }.ToJsonString();
            default:
                lock (_gate)
                {
                    var keys = new JsonArray([.. _publishedKeys.Select(k => k.DeepClone())]);
                    return new JsonObject { ["keys"] = keys }.ToJsonString();
                }
        }
    }

    private sealed class BackchannelHandler(FakeIdentityProvider idp) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (!string.Equals(uri.Host, "idp.test.invalid", StringComparison.OrdinalIgnoreCase))
            {
                lock (idp._gate)
                {
                    idp._unexpectedRequests.Add(uri.GetLeftPart(UriPartial.Path));
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            switch (idp.Mode)
            {
                case FakeIdpMode.ConnectionRefused:
                    throw new HttpRequestException("Connection refused (fake IdP).", new SocketException((int)SocketError.ConnectionRefused));
                case FakeIdpMode.ServiceUnavailable:
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                case FakeIdpMode.Timeout:
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    break;
            }

            if (uri.AbsolutePath == DiscoveryPath)
            {
                Interlocked.Increment(ref idp._discoveryRequests);
                return Json(BuildDiscoveryDocument());
            }

            if (uri.AbsolutePath == "/jwks")
            {
                Interlocked.Increment(ref idp._jwksRequests);
                return Json(idp.BuildJwks());
            }

            lock (idp._gate)
            {
                idp._unexpectedRequests.Add(uri.GetLeftPart(UriPartial.Path));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

/// <summary>Especificação de um JWT de teste. <c>null</c> em um campo significa "omitir a claim/header".</summary>
public sealed class JwtSpec
{
    public string Alg { get; set; } = "RS256";

    /// <summary>Se não definido, o kid padrão do algoritmo é usado. Use <see cref="OmitKid"/> para omitir.</summary>
    public string? Kid { get; set; }

    public bool OmitKid { get; set; }

    public string? Issuer { get; set; } = FakeIdentityProvider.Issuer;

    /// <summary><c>string</c>, <c>string[]</c> ou <c>null</c> (omitido).</summary>
    public object? Audience { get; set; }

    public DateTimeOffset? Expires { get; set; } = DateTimeOffset.UtcNow.AddMinutes(5);

    public DateTimeOffset? NotBefore { get; set; } = DateTimeOffset.UtcNow.AddMinutes(-1);

    public DateTimeOffset? IssuedAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(-1);

    public string Subject { get; set; } = "synthetic-subject-0001";

    public Dictionary<string, object?> ExtraHeader { get; } = [];

    public Dictionary<string, object?> ExtraClaims { get; } = [];

    /// <summary>Chave RSA alternativa (ex.: chave do atacante ou não publicada).</summary>
    public RSA? RsaKeyOverride { get; set; }

    /// <summary>Segredo HMAC para HS*. Padrão: DER da chave pública RSA publicada (ataque de confusão de algoritmo).</summary>
    public byte[]? HmacSecret { get; set; }
}

/// <summary>
/// Emite JWTs de teste (somente para testes; a validação continua sendo do handler JwtBearer de produção).
/// A montagem é manual para permitir mutações que bibliotecas de emissão impedem (alg none, jku, kid malicioso).
/// </summary>
public sealed class JwtFactory(FakeIdentityProvider idp, string audience)
{
    public string Audience { get; } = audience;

    public string Create(Action<JwtSpec>? configure = null)
    {
        var spec = new JwtSpec { Audience = Audience };
        configure?.Invoke(spec);

        var header = new Dictionary<string, object?> { ["alg"] = spec.Alg, ["typ"] = "JWT" };
        if (!spec.OmitKid)
        {
            header["kid"] = spec.Kid ?? DefaultKid(spec.Alg);
        }

        foreach (var (key, value) in spec.ExtraHeader)
        {
            header[key] = value;
        }

        var payload = new Dictionary<string, object?> { ["sub"] = spec.Subject, ["jti"] = Guid.NewGuid().ToString("N") };
        AddIfNotNull(payload, "iss", spec.Issuer);
        AddIfNotNull(payload, "aud", spec.Audience);
        AddIfNotNull(payload, "exp", spec.Expires?.ToUnixTimeSeconds());
        AddIfNotNull(payload, "nbf", spec.NotBefore?.ToUnixTimeSeconds());
        AddIfNotNull(payload, "iat", spec.IssuedAt?.ToUnixTimeSeconds());
        foreach (var (key, value) in spec.ExtraClaims)
        {
            payload[key] = value;
        }

        var signingInput = $"{Encode(header)}.{Encode(payload)}";
        var signature = Sign(spec, Encoding.ASCII.GetBytes(signingInput));
        return $"{signingInput}.{Base64UrlEncoder.Encode(signature)}";
    }

    /// <summary>Altera o payload depois da assinatura (TC-006).</summary>
    public static string TamperPayload(string token, string claim, object value)
    {
        ArgumentNullException.ThrowIfNull(token);
        var parts = token.Split('.');
        var payload = JsonNode.Parse(Base64UrlEncoder.Decode(parts[1]))!.AsObject();
        payload[claim] = JsonValue.Create(value);
        parts[1] = Base64UrlEncoder.Encode(payload.ToJsonString());
        return string.Join('.', parts);
    }

    private byte[] Sign(JwtSpec spec, byte[] data)
    {
        var rsa = spec.RsaKeyOverride ?? idp.Rsa;
        return spec.Alg switch
        {
            "none" => [],
            "RS256" => rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            "RS384" => rsa.SignData(data, HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1),
            "RS512" => rsa.SignData(data, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1),
            "PS256" => rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss),
            "PS384" => rsa.SignData(data, HashAlgorithmName.SHA384, RSASignaturePadding.Pss),
            "ES256" => idp.Ec256.SignData(data, HashAlgorithmName.SHA256),
            "ES384" => idp.Ec384.SignData(data, HashAlgorithmName.SHA384),
            "HS256" => HMACSHA256.HashData(spec.HmacSecret ?? idp.Rsa.ExportSubjectPublicKeyInfo(), data),
            "HS384" => HMACSHA384.HashData(spec.HmacSecret ?? idp.Rsa.ExportSubjectPublicKeyInfo(), data),
            "HS512" => HMACSHA512.HashData(spec.HmacSecret ?? idp.Rsa.ExportSubjectPublicKeyInfo(), data),
            _ => throw new NotSupportedException($"Algoritmo de teste não suportado: {spec.Alg}"),
        };
    }

    private static string DefaultKid(string alg) => alg switch
    {
        "ES256" => FakeIdentityProvider.Ec256Kid,
        "ES384" => FakeIdentityProvider.Ec384Kid,
        _ => FakeIdentityProvider.RsaKid,
    };

    private static void AddIfNotNull(Dictionary<string, object?> target, string key, object? value)
    {
        if (value is not null)
        {
            target[key] = value;
        }
    }

    private static string Encode(Dictionary<string, object?> value) =>
        Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(value));
}
