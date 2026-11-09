using System.Net;
using BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure.HttpAssertions;

namespace BancoCarrefour.Payments.Api.IntegrationTests.Platform;

/// <summary>Problem Details RFC 9457 para 401/404/405/500 e versão não suportada (NFR-134; TC-211).</summary>
public sealed class ProblemDetailsTests : IAsyncLifetime
{
    public const string FaultPath = "/__test/fault";
    public const string ExceptionCanary = "exception-canary-7f3a";

    private PaymentsApiFactory _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new PaymentsApiFactory(
            environment: "Production",
            configureServices: services => services.AddSingleton<IAuthorizationHandler, ThrowingAuthorizationHandler>());
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task UnknownRoute_Authenticated_Returns404ProblemDetails()
    {
        using var response = await SendAsync(_client, Get("/v1/does-not-exist", _factory.Tokens.Create()));

        await ShouldBeProblemDetailsAsync(response, 404);
    }

    [Fact]
    public async Task UnknownRoute_Anonymous_Returns401NotRevealingRouteExistence()
    {
        // Fallback policy: rota inexistente sem token não pode ser distinguida de rota protegida.
        using var response = await SendAsync(_client, Get("/v1/does-not-exist"));

        await ShouldBeProblemDetailsAsync(response, 401);
    }

    [Fact]
    public async Task Ping_WrongMethod_Returns405ProblemDetails()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PingPath);
        request.Headers.Authorization = new("Bearer", _factory.Tokens.Create());

        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        await ShouldBeProblemDetailsAsync(response, 405);
    }

    [Fact]
    public async Task UnhandledException_InProduction_Returns500ProblemDetailsWithoutStackOrMessage()
    {
        // Falha injetada por um IAuthorizationHandler de teste (registrado só neste host de teste) que lança
        // dentro do pipeline, depois do UseExceptionHandler de produção.
        using var response = await SendAsync(_client, Get(FaultPath, _factory.Tokens.Create(), correlationId: "tc211-500"));

        var problem = await ShouldBeProblemDetailsAsync(response, 500);
        var body = problem.GetRawText();
        body.ShouldNotContain(ExceptionCanary);
        body.ShouldNotContain("InvalidOperationException");
        body.ShouldNotContain(" at ");
        body.ShouldNotContain("ThrowingAuthorizationHandler");
        problem.TryGetProperty("detail", out _).ShouldBeFalse();
        problem.GetProperty("correlationId").GetString().ShouldBe("tc211-500");
        CorrelationHeader(response).ShouldBe("tc211-500");
    }

    [Fact]
    public async Task UnsupportedApiVersion_Anonymous_Returns401()
    {
        using var response = await SendAsync(_client, Get("/v2/ping"));

        await ShouldBeProblemDetailsAsync(response, 401);
    }

    [Fact]
    public async Task UnsupportedApiVersion_Authenticated_ReturnsClientErrorProblemDetailsNeverSuccess()
    {
        using var response = await SendAsync(_client, Get("/v2/ping", _factory.Tokens.Create()));

        var status = (int)response.StatusCode;
        status.ShouldBeOneOf(400, 404);
        await ShouldBeProblemDetailsAsync(response, status);
    }

    [Fact]
    public async Task Ping_ValidToken_ReportsSupportedApiVersions()
    {
        using var response = await SendAsync(_client, Get(PingPath, _factory.Tokens.Create()));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("api-supported-versions").ShouldContain("1.0");
    }

    private sealed class ThrowingAuthorizationHandler : IAuthorizationHandler
    {
        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            if (context.Resource is HttpContext http && http.Request.Path.StartsWithSegments(FaultPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(ExceptionCanary);
            }

            return Task.CompletedTask;
        }
    }
}
