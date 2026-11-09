using System.Diagnostics;
using BancoCarrefour.ServiceDefaults.Health;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BancoCarrefour.ServiceDefaults.Observability;

/// <summary>
/// OpenTelemetry (traces, métricas, logs) com propagação W3C Trace Context. Exportação OTLP somente se
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> estiver configurado (variáveis OTEL_* padrão do SDK).
/// Higiene: headers não são capturados; query string removida de spans de entrada e saída; exceções não são
/// gravadas nos spans (mensagens podem conter dados pessoais).
/// </summary>
internal static class ObservabilityExtensions
{
    private const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    // Loga a URL completa (com query string) em Information; mantido em Warning para não vazar credenciais/dados.
    private const string HostingDiagnosticsCategory = "Microsoft.AspNetCore.Hosting.Diagnostics";

    public static TBuilder AddDefaultObservability<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var serviceName = builder.Environment.ApplicationName;

        builder.Logging.AddFilter(HostingDiagnosticsCategory, LogLevel.Warning);
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddSource(serviceName)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = static context => !IsHealthRequest(context);
                    options.RecordException = false;
                    options.EnrichWithHttpRequest = static (activity, _) => RemoveQuery(activity);
                })
                .AddHttpClientInstrumentation(options =>
                {
                    options.RecordException = false;
                    options.EnrichWithHttpRequestMessage = static (activity, request) =>
                    {
                        if (request.RequestUri is { IsAbsoluteUri: true } uri)
                        {
                            activity.SetTag("url.full", uri.GetLeftPart(UriPartial.Path));
                        }

                        RemoveQuery(activity);
                    };
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointKey]))
        {
            otel.UseOtlpExporter();
        }

        return builder;
    }

    private static bool IsHealthRequest(HttpContext context) =>
        context.Request.Path.StartsWithSegments(HealthCheckExtensions.LivenessPath, StringComparison.OrdinalIgnoreCase)
        || context.Request.Path.StartsWithSegments(HealthCheckExtensions.ReadinessPath, StringComparison.OrdinalIgnoreCase);

    private static void RemoveQuery(Activity activity) => activity.SetTag("url.query", null);
}
