using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BancoCarrefour.ServiceDefaults.Security;

/// <summary>
/// Rejeita com 400 qualquer requisição que traga credenciais OAuth na query string (ADR-0006, RFC 9700 / RFC 6750 §2.3).
/// Executa antes da autenticação. O valor do parâmetro nunca é logado nem refletido na resposta.
/// </summary>
internal sealed partial class RejectCredentialsInQueryStringMiddleware(
    RequestDelegate next,
    IProblemDetailsService problemDetailsService,
    ILogger<RejectCredentialsInQueryStringMiddleware> logger)
{
    private static readonly string[] ForbiddenParameters = ["access_token", "id_token", "refresh_token"];

    public async Task InvokeAsync(HttpContext context)
    {
        var query = context.Request.Query;
        if (query.Count > 0)
        {
            foreach (var parameter in ForbiddenParameters)
            {
                if (query.ContainsKey(parameter))
                {
                    LogRejected(logger, parameter);
                    await WriteRejectionAsync(context);
                    return;
                }
            }
        }

        await next(context);
    }

    private async Task WriteRejectionAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Credentials are not accepted in the query string.",
                Detail = "Send the access token in the Authorization header using the Bearer scheme.",
            },
        });
    }

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Warning,
        Message = "Request rejected: credential parameter '{ParameterName}' found in query string.")]
    private static partial void LogRejected(ILogger logger, string parameterName);
}
