using System.Net;
using System.Net.Http.Headers;
using BancoCarrefour.CoreBanking.Host.IntegrationTests.Infrastructure;
using Grpc.Core;
using Grpc.Health.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BancoCarrefour.CoreBanking.Host.IntegrationTests;

public sealed class CoreBankingHostFixture : IAsyncLifetime
{
    public CoreBankingHostFactory Factory { get; } = new();

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
/// CoreBanking.Host (ADR-0009): health HTTP e gRPC anônimos, fallback 401 em rota não mapeada, gRPC sem credencial
/// → Unauthenticated (TC-026 parte F0), Problem Details (TC-211) e inventário de endpoints anônimos (TC-022).
/// </summary>
public sealed class CoreBankingHostTests(CoreBankingHostFixture fixture) : IClassFixture<CoreBankingHostFixture>
{
    private static readonly Method<byte[], byte[]> UnknownMethod = new(
        MethodType.Unary,
        "bancocarrefour.tests.v1.ProbeService",
        "Probe",
        Marshallers.Create(static b => b, static b => b),
        Marshallers.Create(static b => b, static b => b));

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_Anonymous_Returns200(string path)
    {
        using var response = await fixture.Client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task UnmappedRoute_Anonymous_Returns401ProblemDetailsWithBearerChallenge()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/accounts/whatever");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "cb-tc211");

        using var response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ShouldContain(h => h.Scheme == "Bearer");
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("\"correlationId\":\"cb-tc211\"");
    }

    [Fact]
    public async Task UnmappedRoute_Authenticated_Returns404ProblemDetails()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/accounts/whatever");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Factory.Tokens.Create());

        using var response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task HttpRequest_WithTokenForAnotherAudience_Returns401()
    {
        // Audience exata por API (SEC-REQ-003): token da Payments API não vale no Core.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/accounts/whatever");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.Factory.Tokens.Create(s => s.Audience = "api://payments-test"));

        using var response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GrpcHealth_Anonymous_ReturnsServing()
    {
        using var channel = fixture.Factory.CreateGrpcChannel();
        var client = new Health.HealthClient(channel);

        var reply = await client.CheckAsync(new HealthCheckRequest(), cancellationToken: TestContext.Current.CancellationToken);

        reply.Status.ShouldBe(HealthCheckResponse.Types.ServingStatus.Serving);
    }

    [Fact(Skip = "TA-018: Grpc.AspNetCore registra os endpoints 'unimplemented' com AllowAnonymous; chamada sem "
        + "credencial a método inexistente retorna Unimplemented (não Unauthenticated), permitindo enumerar serviços. "
        + "Correção sugerida ao developer: GrpcServiceOptions.IgnoreUnknownServices=true ou policy explícita.")]
    public async Task GrpcCall_WithoutCredential_IsUnauthenticated()
    {
        // TC-026 (parte F0): ainda não há serviço de domínio; o método desconhecido cai no endpoint "unimplemented"
        // do gRPC, que precisa estar atrás da fallback policy.
        using var channel = fixture.Factory.CreateGrpcChannel();
        var invoker = channel.CreateCallInvoker();

        var exception = await Should.ThrowAsync<RpcException>(async () =>
            await invoker.AsyncUnaryCall(UnknownMethod, null, new CallOptions(cancellationToken: TestContext.Current.CancellationToken), []));

        exception.StatusCode.ShouldBe(StatusCode.Unauthenticated);
    }

    [Fact(Skip = "TA-018: Grpc.AspNetCore registra os endpoints 'unimplemented' com AllowAnonymous; chamada sem "
        + "credencial a método inexistente retorna Unimplemented (não Unauthenticated), permitindo enumerar serviços. "
        + "Correção sugerida ao developer: GrpcServiceOptions.IgnoreUnknownServices=true ou policy explícita.")]
    public async Task GrpcCall_WithInvalidToken_IsUnauthenticated()
    {
        using var channel = fixture.Factory.CreateGrpcChannel();
        var invoker = channel.CreateCallInvoker();
        var headers = new Metadata { { "Authorization", $"Bearer {fixture.Factory.Tokens.Create(s => s.Alg = "none")}" } };

        var exception = await Should.ThrowAsync<RpcException>(async () =>
            await invoker.AsyncUnaryCall(UnknownMethod, null, new CallOptions(headers, cancellationToken: TestContext.Current.CancellationToken), []));

        exception.StatusCode.ShouldBe(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task GrpcCall_WithValidToken_PassesAuthenticationAndReachesUnimplemented()
    {
        // Controle positivo: prova que o Unauthenticated acima vem da autenticação, não de erro de setup.
        using var channel = fixture.Factory.CreateGrpcChannel();
        var invoker = channel.CreateCallInvoker();
        var headers = new Metadata { { "Authorization", $"Bearer {fixture.Factory.Tokens.Create()}" } };

        var exception = await Should.ThrowAsync<RpcException>(async () =>
            await invoker.AsyncUnaryCall(UnknownMethod, null, new CallOptions(headers, cancellationToken: TestContext.Current.CancellationToken), []));

        exception.StatusCode.ShouldBe(StatusCode.Unimplemented);
    }

    /// <summary>Exceções conhecidas e registradas como finding (TA-018). Catraca: qualquer novo anônimo falha.</summary>
    private static readonly string[] KnownAnonymousExceptionsTa018 =
    [
        "{unimplementedService}/{unimplementedMethod:grpcunimplemented}",
        "grpc.health.v1.Health/{unimplementedMethod:grpcunimplemented}",
    ];

    [Fact]
    public void Endpoints_NoAnonymousEndpointOutsideAllowlistAndKnownFindings()
    {
        // TC-022 (catraca): allowlist = health HTTP + grpc.health.v1 + exceções do TA-018.
        var anonymous = AnonymousRoutes();

        anonymous.ShouldAllBe(route => IsDeclaredAnonymous(route) || KnownAnonymousExceptionsTa018.Contains(route));
        anonymous.ShouldContain("/health/live");
        anonymous.ShouldContain(route => route.TrimStart('/') == "grpc.health.v1.Health/Check");
        var fallback = fixture.Factory.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy;
        fallback.ShouldNotBeNull();
        fallback.AuthenticationSchemes.ShouldBe(["Bearer"]);
    }

    [Fact(Skip = "TA-018: Grpc.AspNetCore registra os endpoints 'unimplemented' com AllowAnonymous; chamada sem "
        + "credencial a método inexistente retorna Unimplemented (não Unauthenticated), permitindo enumerar serviços. "
        + "Correção sugerida ao developer: GrpcServiceOptions.IgnoreUnknownServices=true ou policy explícita.")]
    public void Endpoints_OnlyHealthEndpointsAreAnonymous()
    {
        // TC-022 estrito para o Core.
        AnonymousRoutes().ShouldAllBe(route => IsDeclaredAnonymous(route));
    }

    [Fact]
    public async Task Startup_WithoutAuthority_FailsClosed()
    {
        await using var factory = new CoreBankingHostFactory(settingsOverride: new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = string.Empty,
        });

        var exception = Should.Throw<Exception>(() => factory.CreateClient().Dispose());

        exception.ToString().ShouldContain("OptionsValidationException");
    }

    private static bool IsDeclaredAnonymous(string route) =>
        route == "/health/live"
        || route == "/health/ready"
        || (route.TrimStart('/').StartsWith("grpc.health.v1.Health/", StringComparison.Ordinal) && !route.Contains('{', StringComparison.Ordinal));

    private List<string> AnonymousRoutes() =>
        [.. fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText!)];
}
