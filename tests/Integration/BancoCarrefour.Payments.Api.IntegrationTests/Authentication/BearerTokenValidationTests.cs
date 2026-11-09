using System.Net;
using System.Security.Cryptography;
using BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure;
using BancoCarrefour.TestKit;
using static BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure.HttpAssertions;

namespace BancoCarrefour.Payments.Api.IntegrationTests.Authentication;

/// <summary>Uma instância da API + IdP fake por classe de teste (chaves novas por classe, §5.1).</summary>
public sealed class PaymentsApiClassFixture : IAsyncLifetime
{
    public PaymentsApiFactory Factory { get; } = new();

    public HttpClient Client { get; private set; } = null!;

    public ValueTask InitializeAsync()
    {
        Client = Factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
    }
}

/// <summary>
/// Suíte negativa de JWT (ADR-0006; SEC-REQ-001..004; TC-001..006, TC-010, TC-011, TC-013, TC-014, TC-024)
/// contra o pipeline real de validação do JwtBearer (TestServer in-process).
/// </summary>
public sealed class BearerTokenValidationTests(PaymentsApiClassFixture fixture) : IClassFixture<PaymentsApiClassFixture>
{
    private const int SkewSeconds = 30;

    private readonly HttpClient _client = fixture.Client;
    private readonly JwtFactory _tokens = fixture.Factory.Tokens;

    public enum Mutation
    {
        AlgNone,
        AlgNoneUppercase,
        Hs256WithRsaPublicKey,
        Hs512WithRsaPublicKey,
        Rs384,
        Rs512,
        Ps384,
        Es384,
        TamperedPayload,
        WrongIssuer,
        IssuerCaseVariant,
        IssuerWithoutTrailingSlash,
        MissingIssuer,
        WrongAudience,
        MissingAudience,
        AudienceListWithoutApi,
        ServiceAudience,
        TerminalAudience,
        ExpiredBeyondSkew,
        MissingExp,
        NotBeforeInFutureBeyondSkew,
        SignedByUnpublishedKey,
        SignatureStripped,
    }

    [Fact]
    public async Task Ping_WithoutAuthorization_Returns401WithBearerChallengeAndProblemDetails()
    {
        // TC-001
        using var response = await SendAsync(_client, Get(PingPath, correlationId: "tc001-corr"));

        var problem = await ShouldBeProblemDetailsAsync(response, 401);
        response.Headers.WwwAuthenticate.ShouldContain(h => h.Scheme == "Bearer");
        response.Headers.WwwAuthenticate.ToString().ShouldNotContain("error_description");
        problem.GetProperty("correlationId").GetString().ShouldBe("tc001-corr");
        var body = problem.GetRawText();
        body.ShouldNotContain("Exception");
        body.ShouldNotContain("IDX");
    }

    [Theory]
    [InlineData("RS256")]
    [InlineData("PS256")]
    [InlineData("ES256")]
    public async Task Ping_WithValidTokenOfAllowedAlgorithm_Returns204(string alg)
    {
        // TC-002 (controle positivo) e TC-005 (ramo permitido)
        var token = _tokens.Create(s => s.Alg = alg);

        (await PingStatusAsync(_client, token)).ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(Mutation.AlgNone)]
    [InlineData(Mutation.AlgNoneUppercase)]
    [InlineData(Mutation.Hs256WithRsaPublicKey)]
    [InlineData(Mutation.Hs512WithRsaPublicKey)]
    [InlineData(Mutation.Rs384)]
    [InlineData(Mutation.Rs512)]
    [InlineData(Mutation.Ps384)]
    [InlineData(Mutation.Es384)]
    [InlineData(Mutation.TamperedPayload)]
    [InlineData(Mutation.WrongIssuer)]
    [InlineData(Mutation.IssuerCaseVariant)]
    [InlineData(Mutation.IssuerWithoutTrailingSlash)]
    [InlineData(Mutation.MissingIssuer)]
    [InlineData(Mutation.WrongAudience)]
    [InlineData(Mutation.MissingAudience)]
    [InlineData(Mutation.AudienceListWithoutApi)]
    [InlineData(Mutation.ServiceAudience)]
    [InlineData(Mutation.TerminalAudience)]
    [InlineData(Mutation.ExpiredBeyondSkew)]
    [InlineData(Mutation.MissingExp)]
    [InlineData(Mutation.NotBeforeInFutureBeyondSkew)]
    [InlineData(Mutation.SignedByUnpublishedKey)]
    [InlineData(Mutation.SignatureStripped)]
    public async Task Ping_WithInvalidToken_Returns401WithoutErrorDetails(Mutation mutation)
    {
        // TC-003, TC-004, TC-005, TC-006, TC-010, TC-011, TC-013, TC-014, TC-024
        using var unpublished = RSA.Create(2048);
        var token = Build(mutation, unpublished);

        using var response = await SendAsync(_client, Get(PingPath, token));

        await ShouldBeProblemDetailsAsync(response, 401);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        challenge.ShouldStartWith("Bearer");
        challenge.ShouldNotContain("error_description");
        challenge.ShouldNotContain("IDX");
    }

    [Fact]
    public async Task Ping_TokenExpiredWithinSkew_Returns204()
    {
        // TC-013 (controle): exp = agora - (skew - 5 s). Margem de 5 s porque o IdentityModel usa o relógio real.
        var token = _tokens.Create(s => s.Expires = DateTimeOffset.UtcNow.AddSeconds(-(SkewSeconds - 5)));

        (await PingStatusAsync(_client, token)).ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Ping_AudienceListContainingApi_Returns204()
    {
        // TC-011 (controle): a lista de audiences contendo a API é aceita; o negativo é a lista sem a API.
        var token = _tokens.Create(s => s.Audience = new[] { "api://other", PaymentsApiFactory.Audience });

        (await PingStatusAsync(_client, token)).ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Ping_BearerSchemeWithEmptyToken_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, PingPath);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer ");

        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ping_BasicScheme_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, PingPath);
        request.Headers.TryAddWithoutValidation("Authorization", "Basic dXNlcjpwYXNz");

        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(Skip = "TA-013: ADR-0006 exige nbf obrigatório; o skeleton aceita token sem nbf (TokenValidationParameters não "
        + "exige nbf). Teste mantido para virar gate quando o developer corrigir ou a ADR for ajustada.")]
    public async Task Ping_TokenWithoutNotBefore_Returns401()
    {
        // TC-014 (variante 'nbf obrigatório', ADR-0006 linha 32)
        var token = _tokens.Create(s => s.NotBefore = null);

        (await PingStatusAsync(_client, token)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    private string Build(Mutation mutation, RSA unpublished) => mutation switch
    {
        Mutation.AlgNone => _tokens.Create(s => s.Alg = "none"),
        Mutation.AlgNoneUppercase => _tokens.Create(s =>
        {
            s.Alg = "none";
            s.ExtraHeader["alg"] = "NONE";
        }),
        Mutation.Hs256WithRsaPublicKey => _tokens.Create(s => s.Alg = "HS256"),
        Mutation.Hs512WithRsaPublicKey => _tokens.Create(s => s.Alg = "HS512"),
        Mutation.Rs384 => _tokens.Create(s => s.Alg = "RS384"),
        Mutation.Rs512 => _tokens.Create(s => s.Alg = "RS512"),
        Mutation.Ps384 => _tokens.Create(s => s.Alg = "PS384"),
        Mutation.Es384 => _tokens.Create(s => s.Alg = "ES384"),
        Mutation.TamperedPayload => JwtFactory.TamperPayload(_tokens.Create(), "sub", "synthetic-subject-9999"),
        Mutation.WrongIssuer => _tokens.Create(s => s.Issuer = "https://evil-idp.test.invalid/"),
        Mutation.IssuerCaseVariant => _tokens.Create(s => s.Issuer = "https://IDP.test.invalid/"),
        Mutation.IssuerWithoutTrailingSlash => _tokens.Create(s => s.Issuer = "https://idp.test.invalid"),
        Mutation.MissingIssuer => _tokens.Create(s => s.Issuer = null),
        Mutation.WrongAudience => _tokens.Create(s => s.Audience = "api://other-api"),
        Mutation.MissingAudience => _tokens.Create(s => s.Audience = null),
        Mutation.AudienceListWithoutApi => _tokens.Create(s => s.Audience = new[] { "api://a", "api://b" }),
        Mutation.ServiceAudience => _tokens.Create(s => s.Audience = "api://corebanking-s2s"),
        Mutation.TerminalAudience => _tokens.Create(s => s.Audience = "api://terminal-gateway"),
        Mutation.ExpiredBeyondSkew => _tokens.Create(s =>
        {
            s.Expires = DateTimeOffset.UtcNow.AddSeconds(-(SkewSeconds + 1));
            s.NotBefore = DateTimeOffset.UtcNow.AddMinutes(-10);
            s.IssuedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        }),
        Mutation.MissingExp => _tokens.Create(s => s.Expires = null),
        Mutation.NotBeforeInFutureBeyondSkew => _tokens.Create(s => s.NotBefore = DateTimeOffset.UtcNow.AddSeconds(SkewSeconds + 60)),
        Mutation.SignedByUnpublishedKey => _tokens.Create(s => s.RsaKeyOverride = unpublished),
        Mutation.SignatureStripped => string.Join('.', _tokens.Create().Split('.').Take(2)) + ".",
        _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
    };
}
