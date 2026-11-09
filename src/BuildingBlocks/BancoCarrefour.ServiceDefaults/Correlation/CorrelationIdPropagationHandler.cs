using Microsoft.AspNetCore.Http;

namespace BancoCarrefour.ServiceDefaults.Correlation;

/// <summary>Propaga o correlation-id da requisição de entrada para chamadas HTTP de saída via HttpClientFactory.</summary>
internal sealed class CorrelationIdPropagationHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var correlationId = httpContextAccessor.HttpContext?.GetCorrelationId();
        if (correlationId is not null && !request.Headers.Contains(CorrelationIdFeature.HeaderName))
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdFeature.HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
