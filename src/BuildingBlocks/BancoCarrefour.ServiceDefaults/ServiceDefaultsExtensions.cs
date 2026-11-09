using BancoCarrefour.ServiceDefaults.Authentication;
using BancoCarrefour.ServiceDefaults.Correlation;
using BancoCarrefour.ServiceDefaults.ErrorHandling;
using BancoCarrefour.ServiceDefaults.Health;
using BancoCarrefour.ServiceDefaults.Http;
using BancoCarrefour.ServiceDefaults.Observability;
using BancoCarrefour.ServiceDefaults.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BancoCarrefour.ServiceDefaults;

/// <summary>
/// Padrões transversais obrigatórios para todo serviço HTTP/gRPC da plataforma:
/// Bearer/OIDC (ADR-0006), Problem Details, correlation-id, OpenTelemetry, health checks e resiliência de HttpClient.
/// </summary>
public static class ServiceDefaultsExtensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Configure<KestrelServerOptions>(options => options.AddServerHeader = false);

        builder.AddDefaultObservability();
        builder.AddDefaultBearerAuthentication();
        builder.Services.AddDefaultProblemDetails();
        builder.Services.AddDefaultHealthChecks();
        builder.Services.AddDefaultHttpClientResilience();

        return builder;
    }

    /// <summary>
    /// Pipeline transversal. Deve ser chamado logo após <c>Build()</c> e antes do mapeamento de endpoints.
    /// Ordem: correlation-id, tratamento de exceções, Problem Details para status sem corpo,
    /// rejeição de credenciais na query string, autenticação e autorização.
    /// </summary>
    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseMiddleware<RejectCredentialsInQueryStringMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }

    /// <summary>Mapeia endpoints operacionais anônimos (health checks).</summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapDefaultHealthChecks();

        return app;
    }
}
