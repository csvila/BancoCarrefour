using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Esquema "atacante" registrado só nos testes de DEV-022: autentica qualquer requisição (sem token) e responde ao
/// challenge com um status inconfundível (418). Se o esquema Bearer encaminhar (Forward*) para ele, o teste vê 204/418
/// em vez de 401.
/// </summary>
public sealed class AlwaysAllowAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AlwaysAllow";
    public const int ChallengeStatus = 418;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity([new Claim("sub", "synthetic-forwarded-subject")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = ChallengeStatus;
        return Task.CompletedTask;
    }
}
