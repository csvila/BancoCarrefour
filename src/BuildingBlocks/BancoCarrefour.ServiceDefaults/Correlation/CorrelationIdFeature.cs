using Microsoft.AspNetCore.Http;

namespace BancoCarrefour.ServiceDefaults.Correlation;

/// <summary>Correlation-id opaco da requisição corrente. Nunca derivado de CPF, conta, cartão ou token.</summary>
public interface ICorrelationIdFeature
{
    string CorrelationId { get; }
}

public sealed class CorrelationIdFeature(string correlationId) : ICorrelationIdFeature
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ActivityTagName = "app.correlation_id";
    public const string LogScopeKey = "CorrelationId";

    public string CorrelationId { get; } = correlationId;
}

public static class CorrelationIdHttpContextExtensions
{
    public static string? GetCorrelationId(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Features.Get<ICorrelationIdFeature>()?.CorrelationId;
    }
}
