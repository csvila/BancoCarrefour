using System.Buffers;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace BancoCarrefour.ServiceDefaults.Correlation;

/// <summary>
/// Lê <c>X-Correlation-Id</c> (opaco, validado contra log injection) ou gera um novo; expõe via feature,
/// escopo de log, tag da Activity e header de resposta. O trace context W3C (traceparent) é tratado pelo OpenTelemetry.
/// </summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    private const int MaxLength = 64;

    private static readonly SearchValues<char> AllowedChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.:");

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Resolve(context.Request.Headers[CorrelationIdFeature.HeaderName]);

        context.Features.Set<ICorrelationIdFeature>(new CorrelationIdFeature(correlationId));
        Activity.Current?.SetTag(CorrelationIdFeature.ActivityTagName, correlationId);

        // OnStarting sobrevive ao Response.Clear() do exception handler.
        context.Response.OnStarting(
            static state =>
            {
                var (httpContext, id) = ((HttpContext, string))state;
                httpContext.Response.Headers[CorrelationIdFeature.HeaderName] = id;
                return Task.CompletedTask;
            },
            (context, correlationId));

        using (logger.BeginScope(new Dictionary<string, object> { [CorrelationIdFeature.LogScopeKey] = correlationId }))
        {
            await next(context);
        }
    }

    private static string Resolve(StringValues headerValues)
    {
        if (headerValues.Count == 1)
        {
            var candidate = headerValues[0];
            if (!string.IsNullOrEmpty(candidate)
                && candidate.Length <= MaxLength
                && !candidate.AsSpan().ContainsAnyExcept(AllowedChars))
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N");
    }
}
