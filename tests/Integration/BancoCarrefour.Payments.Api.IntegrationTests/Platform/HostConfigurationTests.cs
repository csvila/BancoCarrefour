using System.Net;
using System.Text.Json;
using BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using static BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure.HttpAssertions;

namespace BancoCarrefour.Payments.Api.IntegrationTests.Platform;

/// <summary>
/// Health (TC-214), inventário de autorização de endpoints (TC-022), OpenAPI (TC-215) e startup fail-closed
/// (TC-218; ADR-0006 "o serviço não sobe sem Authority/Audience").
/// </summary>
public sealed class HostConfigurationTests
{
    private const string FailingDependencyCanary = "conn-string-canary-91b2";

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_Anonymous_Returns200WithMinimalBody(string path)
    {
        await using var factory = new PaymentsApiFactory();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, Get(path));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
        response.Headers.Contains("api-supported-versions").ShouldBeFalse();
    }

    [Fact]
    public async Task Health_ReadyDependencyDown_ReadyIs503AndLiveStays200WithoutDetails()
    {
        await using var factory = new PaymentsApiFactory(configureServices: services =>
            services.AddHealthChecks().AddCheck(
                "fake-dependency",
                () => HealthCheckResult.Unhealthy(FailingDependencyCanary, new InvalidOperationException(FailingDependencyCanary)),
                tags: ["ready"]));
        using var client = factory.CreateClient();

        using var ready = await SendAsync(client, Get("/health/ready"));
        using var live = await SendAsync(client, Get("/health/live"));

        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var readyBody = await ready.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        readyBody.ShouldBe("Unhealthy");
        readyBody.ShouldNotContain(FailingDependencyCanary);
        live.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Endpoints_AllRequireAuthorizationExceptDeclaredAnonymousAllowlist(string environment)
    {
        // TC-022: enumera EndpointDataSource. Anônimos só na allowlist; todo o resto coberto por policy
        // (fallback policy exige usuário autenticado no esquema Bearer).
        await using var factory = new PaymentsApiFactory(environment);
        using var client = factory.CreateClient();
        string[] allowlist = environment == "Development"
            ? ["/health/live", "/health/ready", "/openapi/{documentName}.json"]
            : ["/health/live", "/health/ready"];

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        endpoints.ShouldNotBeEmpty();
        var anonymous = endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText)
            .ToList();
        anonymous.ShouldAllBe(route => allowlist.Contains(route));

        var options = factory.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        options.FallbackPolicy.ShouldNotBeNull();
        options.FallbackPolicy.AuthenticationSchemes.ShouldBe(["Bearer"]);
        options.FallbackPolicy.Requirements.ShouldContain(r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task OpenApi_InDevelopment_IsAnonymousAndListsOnlyV1Routes()
    {
        // TC-215 (parte implementada pelo skeleton).
        await using var factory = new PaymentsApiFactory("Development");
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, Get("/openapi/v1.json"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        paths.ShouldNotBeEmpty();
        paths.ShouldAllBe(p => p.StartsWith("/v1/", StringComparison.Ordinal));
    }

    [Fact(Skip = "TA-017: o documento OpenAPI v1 do skeleton não declara components.securitySchemes (Bearer) nem "
        + "respostas application/problem+json (TC-215, NFR-134). Reativar quando o developer incluir.")]
    public async Task OpenApi_InDevelopment_DeclaresBearerSecuritySchemeAndProblemDetailsErrors()
    {
        // TC-215 (segurança/erros no contrato).
        await using var factory = new PaymentsApiFactory("Development");
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, Get("/openapi/v1.json"));
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);

        document.RootElement.TryGetProperty("components", out var components).ShouldBeTrue("OpenAPI sem 'components'.");
        components.TryGetProperty("securitySchemes", out var schemes).ShouldBeTrue("OpenAPI sem 'securitySchemes'.");
        schemes.EnumerateObject().ShouldContain(s =>
            s.Value.GetProperty("type").GetString() == "http" && s.Value.GetProperty("scheme").GetString() == "bearer");
        json.ShouldContain("application/problem+json");
    }

    [Fact]
    public async Task OpenApi_InProduction_IsNotExposedAnonymously()
    {
        await using var factory = new PaymentsApiFactory("Production");
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, Get("/openapi/v1.json"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    public static TheoryData<string, string, string?> InvalidStartupSettings() => new()
    {
        { "Production", "Authentication:Authority", string.Empty },
        { "Production", "Authentication:Authority", "idp.test.invalid" },
        { "Production", "Authentication:Authority", "http://idp.test.invalid/" },
        { "Production", "Authentication:Audience", string.Empty },
        { "Production", "Authentication:Audience", "   " },
        { "Production", "Authentication:ClockSkewSeconds", "61" },
        { "Production", "Authentication:ClockSkewSeconds", "-1" },
        { "Staging", "Authentication:Authority", "http://idp.test.invalid/" },
        { "Development", "Authentication:Authority", "http://idp.test.invalid/" },
    };

    [Theory]
    [MemberData(nameof(InvalidStartupSettings))]
    public async Task Startup_WithInvalidBearerConfiguration_FailsClosed(string environment, string key, string? value)
    {
        await using var factory = new PaymentsApiFactory(environment, settingsOverride: new Dictionary<string, string?> { [key] = value });

        var exception = Should.Throw<Exception>(() => factory.CreateClient().Dispose());

        ShouldHaveFailedOnOptionsValidation(factory, exception);
    }

    [Fact]
    public async Task Startup_ProductionWithInsecureMetadataFlag_StillFailsForHttpAuthority()
    {
        // TC-218: a flag de desenvolvimento não pode liberar metadata HTTP em Production.
        await using var factory = new PaymentsApiFactory("Production", settingsOverride: new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = "http://idp.test.invalid/",
            ["Authentication:AllowInsecureMetadataInDevelopment"] = "true",
        });

        var exception = Should.Throw<Exception>(() => factory.CreateClient().Dispose());

        ShouldHaveFailedOnOptionsValidation(factory, exception);
    }

    [Fact]
    public async Task Startup_DevelopmentWithInsecureMetadataFlag_AllowsHttpAuthority()
    {
        // Controle do TC-218: o relaxamento existe apenas em Development com opt-in explícito.
        await using var factory = new PaymentsApiFactory("Development", settingsOverride: new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = "http://idp.test.invalid/",
            ["Authentication:AllowInsecureMetadataInDevelopment"] = "true",
        });

        using var client = factory.CreateClient();
        using var response = await SendAsync(client, Get("/health/live"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ping_TokenForAudienceInjectedViaSchemesBinding_Returns401()
    {
        // TA-019 ponta a ponta (gate): configuração adicional em Authentication:Schemes:Bearer não pode ampliar
        // audiences. Controle positivo no mesmo host: o token da audience validada continua aceito.
        await using var factory = new PaymentsApiFactory(settingsOverride: new Dictionary<string, string?>
        {
            ["Authentication:Schemes:Bearer:ValidAudiences:0"] = "api://injected-audience",
        });
        using var client = factory.CreateClient();

        (await PingStatusAsync(client, factory.Tokens.Create())).ShouldBe(HttpStatusCode.NoContent);
        var status = await PingStatusAsync(client, factory.Tokens.Create(s => s.Audience = "api://injected-audience"));

        status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// O startup precisa falhar por <see cref="OptionsValidationException"/>. TA-021: sob carga (os 4 projetos de teste
    /// em paralelo), o <c>DeferredHost</c> do Microsoft.AspNetCore.Mvc.Testing às vezes perde a corrida com o
    /// encerramento do Program e lança <see cref="ObjectDisposedException"/> (IServiceProvider) no lugar da exceção
    /// original. A causa é sempre conferida no log "Hosting failed to start" capturado do próprio host (também no
    /// caminho normal, para que esse oráculo seja exercitado em toda execução); a exceção lançada precisa ser a
    /// original ou, só na corrida conhecida, a ObjectDisposedException vinda do DeferredHost.
    /// </summary>
    private static void ShouldHaveFailedOnOptionsValidation(PaymentsApiFactory factory, Exception exception)
    {
        factory.Logs.Entries.ShouldContain(
            e => e.Category == "Microsoft.Extensions.Hosting.Internal.Host"
                && e.Exception != null
                && e.Exception.Contains(nameof(OptionsValidationException), StringComparison.Ordinal),
            $"Host sem OptionsValidationException registrada no log de startup. Exceção lançada: {exception}");

        if (Flatten(exception).Any(e => e is OptionsValidationException))
        {
            return;
        }

        exception.ShouldBeOfType<ObjectDisposedException>($"Esperado OptionsValidationException; obtido: {exception}");
        (exception.StackTrace ?? string.Empty).ShouldContain("DeferredHost", Case.Sensitive, $"ObjectDisposedException fora da corrida conhecida: {exception}");
    }

    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Flatten))
                {
                    yield return inner;
                }
            }
        }
    }
}
