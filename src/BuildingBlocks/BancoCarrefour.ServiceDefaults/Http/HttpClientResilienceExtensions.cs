using BancoCarrefour.ServiceDefaults.Correlation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace BancoCarrefour.ServiceDefaults.Http;

/// <summary>
/// Resiliência padrão para todo HttpClient criado via IHttpClientFactory:
/// timeout total e por tentativa explícitos, retry limitado com backoff exponencial e jitter, circuit breaker.
/// <para>
/// Retry desabilitado para métodos não seguros (POST, PUT, PATCH, DELETE, CONNECT): nenhum retry cego de operação
/// que possa ter efeito financeiro. Comandos financeiros só podem ser repetidos explicitamente pelo chamador com a
/// mesma Idempotency-Key (ADR-0003). Clientes gRPC (sempre POST) herdam a regra; usar deadlines gRPC.
/// </para>
/// </summary>
internal static class HttpClientResilienceExtensions
{
    public static IServiceCollection AddDefaultHttpClientResilience(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationIdPropagationHandler>();

        services.ConfigureHttpClientDefaults(http =>
        {
            http.AddHttpMessageHandler<CorrelationIdPropagationHandler>();
            http.AddStandardResilienceHandler(ConfigureStandardResilience);
        });

        return services;
    }

    private static void ConfigureStandardResilience(HttpStandardResilienceOptions options)
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);

        options.Retry.MaxRetryAttempts = 2;
        options.Retry.UseJitter = true;
        options.Retry.DisableForUnsafeHttpMethods();

        // Precisa ser >= 2x o timeout por tentativa (validação do pacote).
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    }
}
