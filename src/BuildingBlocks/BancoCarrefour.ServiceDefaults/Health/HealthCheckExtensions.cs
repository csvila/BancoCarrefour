using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BancoCarrefour.ServiceDefaults.Health;

/// <summary>
/// <c>/health/live</c>: só checks com tag "live" (sem dependências externas; reinício resolve).
/// <c>/health/ready</c>: todos os checks (dependências devem ser registradas com tag "ready").
/// Ambos anônimos e sem detalhes no corpo.
/// </summary>
public static class HealthCheckExtensions
{
    public const string LivenessPath = "/health/live";
    public const string ReadinessPath = "/health/ready";
    public const string LiveTag = "live";
    public const string ReadyTag = "ready";

    internal static IServiceCollection AddDefaultHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck("self", static () => HealthCheckResult.Healthy(), tags: [LiveTag]);

        return services;
    }

    internal static WebApplication MapDefaultHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks(LivenessPath, new HealthCheckOptions
        {
            Predicate = static registration => registration.Tags.Contains(LiveTag),
        }).AllowAnonymous();

        app.MapHealthChecks(ReadinessPath, new HealthCheckOptions
        {
            Predicate = static _ => true,
        }).AllowAnonymous();

        return app;
    }
}
