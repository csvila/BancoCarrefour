using BancoCarrefour.ServiceDefaults.Correlation;
using Microsoft.Extensions.DependencyInjection;

namespace BancoCarrefour.ServiceDefaults.ErrorHandling;

/// <summary>
/// Problem Details (RFC 9457) para erros e status sem corpo. Em produção o exception handler não expõe
/// stack trace nem mensagem de exceção; a página de detalhes só existe em Development (padrão do WebApplication).
/// </summary>
internal static class ProblemDetailsExtensions
{
    public static IServiceCollection AddDefaultProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                // traceId é adicionado pelo framework; correlationId é opaco e seguro para o cliente.
                var correlationId = context.HttpContext.GetCorrelationId();
                if (correlationId is not null)
                {
                    context.ProblemDetails.Extensions["correlationId"] = correlationId;
                }

                // Defesa em profundidade: nunca refletir detalhes de exceção ao cliente.
                if (context.Exception is not null)
                {
                    context.ProblemDetails.Detail = null;
                }
            });

        return services;
    }
}
