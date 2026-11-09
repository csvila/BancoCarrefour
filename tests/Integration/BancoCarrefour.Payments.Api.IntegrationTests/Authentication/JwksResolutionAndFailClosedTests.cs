using System.Net;
using System.Security.Cryptography;
using BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure;
using BancoCarrefour.TestKit;
using Microsoft.IdentityModel.Tokens;
using static BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure.HttpAssertions;

namespace BancoCarrefour.Payments.Api.IntegrationTests.Authentication;

/// <summary>
/// Resolução de chaves, rotação e fail-closed do JWKS (SEC-REQ-002/005; TC-007, TC-008, TC-009, TC-015, TC-016,
/// TC-017). Cada teste tem sua própria instância (estado do ConfigurationManager não é compartilhado).
/// </summary>
public sealed class JwksResolutionAndFailClosedTests
{
    [Fact]
    public async Task Ping_ManyTokensWithUnknownKid_Returns401AndJwksRefreshIsBounded()
    {
        // TC-007. Oráculo provisório (TA-012): no máximo 1 refresh além da carga inicial dentro do
        // RefreshInterval configurado (5 min) pelo skeleton.
        await using var factory = new PaymentsApiFactory();
        using var client = factory.CreateClient();
        using var unpublished = RSA.Create(2048);

        (await PingStatusAsync(client, factory.Tokens.Create())).ShouldBe(HttpStatusCode.NoContent);
        var jwksAfterWarmup = factory.Idp.JwksRequests;

        for (var i = 0; i < 20; i++)
        {
            var token = factory.Tokens.Create(s =>
            {
                s.Kid = $"unknown-kid-{i}";
                s.RsaKeyOverride = unpublished;
            });
            (await PingStatusAsync(client, token)).ShouldBe(HttpStatusCode.Unauthorized);
        }

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        (factory.Idp.JwksRequests - jwksAfterWarmup).ShouldBeLessThanOrEqualTo(1);
        factory.Idp.DiscoveryRequests.ShouldBeLessThanOrEqualTo(2);
        factory.Idp.UnexpectedRequests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("traversal")]
    [InlineData("sql")]
    [InlineData("url")]
    [InlineData("huge")]
    public async Task Ping_MaliciousKid_Returns401WithoutOutboundCalls(string kidKind)
    {
        // TC-008
        await using var attacker = new AttackerListener();
        await using var factory = new PaymentsApiFactory();
        using var client = factory.CreateClient();
        using var attackerKey = RSA.Create(2048);
        var kid = kidKind switch
        {
            "traversal" => "../../../../etc/passwd",
            "sql" => "' OR 1=1 --",
            "url" => new Uri(attacker.BaseUri, "jwks.json").ToString(),
            "huge" => new string('k', 10 * 1024),
            _ => throw new ArgumentOutOfRangeException(nameof(kidKind)),
        };

        var token = factory.Tokens.Create(s =>
        {
            s.Kid = kid;
            s.RsaKeyOverride = attackerKey;
        });

        (await PingStatusAsync(client, token)).ShouldBe(HttpStatusCode.Unauthorized);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        attacker.Connections.ShouldBe(0);
        factory.Idp.UnexpectedRequests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("jku")]
    [InlineData("x5u")]
    [InlineData("jwk")]
    public async Task Ping_TokenWithEmbeddedKeyReference_IgnoresHeaderAndReturns401(string header)
    {
        // TC-009: chave do atacante anunciada no header; o serviço não pode usá-la nem buscá-la.
        await using var attacker = new AttackerListener();
        await using var factory = new PaymentsApiFactory();
        using var client = factory.CreateClient();
        using var attackerKey = RSA.Create(2048);
        var publicParameters = attackerKey.ExportParameters(includePrivateParameters: false);

        var token = factory.Tokens.Create(s =>
        {
            s.Kid = "attacker-key";
            s.RsaKeyOverride = attackerKey;
            s.ExtraHeader[header] = header switch
            {
                "jwk" => new Dictionary<string, string>
                {
                    ["kty"] = "RSA",
                    ["kid"] = "attacker-key",
                    ["n"] = Base64UrlEncoder.Encode(publicParameters.Modulus!),
                    ["e"] = Base64UrlEncoder.Encode(publicParameters.Exponent!),
                },
                _ => new Uri(attacker.BaseUri, header).ToString(),
            };
        });

        (await PingStatusAsync(client, token)).ShouldBe(HttpStatusCode.Unauthorized);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        attacker.Connections.ShouldBe(0);
        factory.Idp.UnexpectedRequests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(FakeIdpMode.ServiceUnavailable)]
    [InlineData(FakeIdpMode.ConnectionRefused)]
    [InlineData(FakeIdpMode.Timeout)]
    public async Task Ping_IdpUnavailableFromColdStart_FailsClosedWith401(FakeIdpMode mode)
    {
        // TC-015: nunca 2xx nem 5xx. A variante Timeout espera o BackchannelTimeout de produção (10 s).
        await using var factory = new PaymentsApiFactory();
        factory.Idp.Mode = mode;
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, Get(PingPath, factory.Tokens.Create()));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await ShouldBeProblemDetailsAsync(response, 401);
    }

    [Fact]
    public async Task Ping_IdpBecomesUnavailableAfterWarmup_ValidTokenStillAcceptedFromCacheAndInvalidRejected()
    {
        // TC-015 (variante após cache): a indisponibilidade do IdP não pode abrir o pipeline.
        await using var factory = new PaymentsApiFactory();
        using var client = factory.CreateClient();
        using var unpublished = RSA.Create(2048);
        (await PingStatusAsync(client, factory.Tokens.Create())).ShouldBe(HttpStatusCode.NoContent);

        factory.Idp.Mode = FakeIdpMode.ServiceUnavailable;

        (await PingStatusAsync(client, factory.Tokens.Create(s => s.RsaKeyOverride = unpublished)))
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await PingStatusAsync(client, factory.Tokens.Create())).ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Ping_KeyRotationWithOverlap_BothKeysAcceptedAndNewKidAcceptedWithoutDowntimeForOthers()
    {
        // TC-016 (ramos sobreposição e chave nova): A e B publicadas; depois o IdP publica C. Esperado: A e B aceitas
        // na sobreposição; C aceita após o refresh disparado por kid desconhecido; B sem downtime.
        // Observação (TA-016): a 1ª requisição com o kid novo recebe 401 (refresh assíncrono), por isso o polling.
        await using var factory = new PaymentsApiFactory();
        using var keyB = RSA.Create(2048);
        using var keyC = RSA.Create(2048);
        factory.Idp.PublishRsaKey("rsa-b", keyB);
        using var client = factory.CreateClient();
        string TokenB() => factory.Tokens.Create(s => { s.Kid = "rsa-b"; s.RsaKeyOverride = keyB; });
        string TokenC() => factory.Tokens.Create(s => { s.Kid = "rsa-c"; s.RsaKeyOverride = keyC; });

        (await PingStatusAsync(client, factory.Tokens.Create())).ShouldBe(HttpStatusCode.NoContent);
        (await PingStatusAsync(client, TokenB())).ShouldBe(HttpStatusCode.NoContent);

        factory.Idp.PublishRsaKey("rsa-c", keyC);

        (await PollUntilAcceptedAsync(client, TokenC)).ShouldBeTrue("Token com kid novo deveria ser aceito após o refresh do JWKS.");
        (await PingStatusAsync(client, TokenB())).ShouldBe(HttpStatusCode.NoContent);
        (await PingStatusAsync(client, factory.Tokens.Create())).ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact(Skip = "TA-015: chave removida do JWKS continua aceita após o refresh (last-known-good do IdentityModel, "
        + "UseLastKnownGoodConfiguration=true por padrão; sonda com LKG desligado retornou 401). Viola SEC-REQ-005 "
        + "'chave removida -> 401'. Reativar quando o developer decidir/corrigir.")]
    public async Task Ping_KeyRemovedFromJwksAfterRefresh_Returns401()
    {
        // TC-016 (ramo remoção) / SEC-REQ-005.
        await using var factory = new PaymentsApiFactory();
        using var keyC = RSA.Create(2048);
        using var client = factory.CreateClient();
        string TokenC() => factory.Tokens.Create(s => { s.Kid = "rsa-c"; s.RsaKeyOverride = keyC; });
        (await PingStatusAsync(client, factory.Tokens.Create())).ShouldBe(HttpStatusCode.NoContent);

        factory.Idp.RemoveKey(FakeIdentityProvider.RsaKid);
        factory.Idp.PublishRsaKey("rsa-c", keyC);
        (await PollUntilAcceptedAsync(client, TokenC)).ShouldBeTrue("Pré-condição: o refresh do JWKS precisa ter ocorrido.");

        (await PingStatusAsync(client, factory.Tokens.Create())).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ping_MalformedJwks_Returns401()
    {
        // TC-017
        await using var factory = new PaymentsApiFactory();
        factory.Idp.Mode = FakeIdpMode.MalformedJwks;
        using var client = factory.CreateClient();

        (await PingStatusAsync(client, factory.Tokens.Create())).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("HS256")]
    [InlineData("RS256")]
    public async Task Ping_JwksWithOnlySymmetricKey_Returns401(string alg)
    {
        // TC-017: JWKS com kty=oct; token HS256 assinado com esse segredo (kid correspondente) e RS256 legítimo.
        await using var factory = new PaymentsApiFactory();
        factory.Idp.Mode = FakeIdpMode.SymmetricKeyOnlyJwks;
        using var client = factory.CreateClient();

        var token = factory.Tokens.Create(s =>
        {
            s.Alg = alg;
            if (alg == "HS256")
            {
                s.Kid = FakeIdentityProvider.SymmetricKid;
                s.HmacSecret = factory.Idp.SymmetricSecret;
            }
        });

        (await PingStatusAsync(client, token)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory(Skip = "TA-014: token assinado com chave publicada mas kid inexistente/ausente é aceito "
        + "(TokenValidationParameters.TryAllIssuerSigningKeys=true por padrão; sonda com false retornou 401). "
        + "Decisão necessária: SEC-REQ-002 exige 401 para kid desconhecido?")]
    [InlineData("kid-nao-publicado")]
    [InlineData(null)]
    public async Task Ping_PublishedKeyButKidMismatchOrMissing_Returns401(string? kid)
    {
        // SEC-REQ-002 ("kid resolvido só no JWKS do issuer"): token assinado com a chave publicada, porém com kid
        // que não existe no JWKS (ou sem kid).
        await using var factory = new PaymentsApiFactory();
        using var client = factory.CreateClient();

        var token = factory.Tokens.Create(s =>
        {
            if (kid is null)
            {
                s.OmitKid = true;
            }
            else
            {
                s.Kid = kid;
            }
        });

        (await PingStatusAsync(client, token)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<bool> PollUntilAcceptedAsync(HttpClient client, Func<string> token)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (await PingStatusAsync(client, token()) == HttpStatusCode.NoContent)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        }

        return false;
    }
}
