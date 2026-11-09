using System.Text.Json;
using BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace BancoCarrefour.Payments.Api.IntegrationTests.Playwright;

/// <summary>
/// Payments API real em Kestrel (porta efêmera de loopback, via <c>WebApplicationFactory.UseKestrel(0)</c> do .NET 10)
/// exercitada por Playwright <see cref="IAPIRequestContext"/> sobre HTTP real. Não requer navegadores.
/// </summary>
public sealed class KestrelPlaywrightFixture : IAsyncLifetime
{
    public PaymentsApiFactory Factory { get; } = new();

    public IPlaywright Playwright { get; private set; } = null!;

    public IAPIRequestContext Api { get; private set; } = null!;

    public Uri BaseAddress { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Factory.UseKestrel(0);
        Factory.StartServer();
        var addresses = Factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        BaseAddress = new Uri(addresses.First(a => a.StartsWith("http://", StringComparison.Ordinal)));

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Api = await Playwright.APIRequest.NewContextAsync(new APIRequestNewContextOptions
        {
            BaseURL = BaseAddress.ToString(),
            IgnoreHTTPSErrors = false,
            Timeout = 15_000,
        });
    }

    public async ValueTask DisposeAsync()
    {
        await Api.DisposeAsync();
        Playwright.Dispose();
        await Factory.DisposeAsync();
    }
}

public sealed class PlaywrightApiTests(KestrelPlaywrightFixture fixture) : IClassFixture<KestrelPlaywrightFixture>
{
    private readonly IAPIRequestContext _api = fixture.Api;

    [Fact]
    public void Server_ListensOnLoopbackOverRealHttp()
    {
        fixture.BaseAddress.IsLoopback.ShouldBeTrue();
        fixture.BaseAddress.Port.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Ping_WithoutToken_Returns401ChallengeProblemDetailsAndCorrelationId()
    {
        // TC-001 sobre HTTP real
        var response = await _api.GetAsync("/v1/ping", new() { Headers = new Dictionary<string, string> { ["X-Correlation-Id"] = "pw-tc001" } });

        response.Status.ShouldBe(401);
        response.Headers["www-authenticate"].ShouldStartWith("Bearer");
        response.Headers["content-type"].ShouldStartWith("application/problem+json");
        response.Headers["x-correlation-id"].ShouldBe("pw-tc001");
        response.Headers.ContainsKey("server").ShouldBeFalse("Kestrel não deve anunciar o header Server.");
        var problem = (await response.JsonAsync())!.Value;
        problem.GetProperty("status").GetInt32().ShouldBe(401);
        problem.GetProperty("correlationId").GetString().ShouldBe("pw-tc001");
    }

    [Theory]
    [InlineData("RS256")]
    [InlineData("PS256")]
    [InlineData("ES256")]
    public async Task Ping_WithValidToken_Returns204(string alg)
    {
        // TC-002 sobre HTTP real
        var token = fixture.Factory.Tokens.Create(s => s.Alg = alg);

        var response = await _api.GetAsync("/v1/ping", new() { Headers = Bearer(token) });

        response.Status.ShouldBe(204);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("HS256")]
    [InlineData("RS512")]
    public async Task Ping_WithDisallowedAlgorithm_Returns401(string alg)
    {
        // TC-003/TC-004/TC-005 sobre HTTP real
        var token = fixture.Factory.Tokens.Create(s => s.Alg = alg);

        var response = await _api.GetAsync("/v1/ping", new() { Headers = Bearer(token) });

        response.Status.ShouldBe(401);
        response.Headers["www-authenticate"].ShouldNotContain("error_description");
    }

    [Fact]
    public async Task Ping_WithExpiredTokenBeyondSkew_Returns401()
    {
        var token = fixture.Factory.Tokens.Create(s => s.Expires = DateTimeOffset.UtcNow.AddSeconds(-31));

        var response = await _api.GetAsync("/v1/ping", new() { Headers = Bearer(token) });

        response.Status.ShouldBe(401);
    }

    [Fact]
    public async Task Ping_WithAccessTokenInQuery_Returns400ProblemDetails()
    {
        // TC-018 sobre HTTP real (inclui header válido junto)
        var token = fixture.Factory.Tokens.Create();

        var response = await _api.GetAsync("/v1/ping", new()
        {
            Params = new Dictionary<string, object> { ["access_token"] = token },
            Headers = Bearer(token),
        });

        response.Status.ShouldBe(400);
        response.Headers["content-type"].ShouldStartWith("application/problem+json");
        (await response.TextAsync()).ShouldNotContain(token);
    }

    [Fact]
    public async Task Ping_WithValidCorrelationId_EchoesIt()
    {
        // TC-212 sobre HTTP real
        var token = fixture.Factory.Tokens.Create();
        var headers = Bearer(token);
        headers["X-Correlation-Id"] = "pw-corr-0001";

        var response = await _api.GetAsync("/v1/ping", new() { Headers = headers });

        response.Status.ShouldBe(204);
        response.Headers["x-correlation-id"].ShouldBe("pw-corr-0001");
    }

    [Fact]
    public async Task Ping_WithMalformedCorrelationId_ReplacesIt()
    {
        var response = await _api.GetAsync("/v1/ping", new() { Headers = new Dictionary<string, string> { ["X-Correlation-Id"] = "<b>x</b>" } });

        response.Headers["x-correlation-id"].ShouldMatch("^[0-9a-f]{32}$");
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_Anonymous_Returns200(string path)
    {
        // TC-214 sobre HTTP real
        var response = await _api.GetAsync(path);

        response.Status.ShouldBe(200);
        (await response.TextAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task UnsupportedVersion_Anonymous_Returns401()
    {
        var response = await _api.GetAsync("/v2/ping");

        response.Status.ShouldBe(401);
        var problem = (await response.JsonAsync())!.Value;
        problem.ValueKind.ShouldBe(JsonValueKind.Object);
    }

    private static Dictionary<string, string> Bearer(string token) => new() { ["Authorization"] = $"Bearer {token}" };
}
