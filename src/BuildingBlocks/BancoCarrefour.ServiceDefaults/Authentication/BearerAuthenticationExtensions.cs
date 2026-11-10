using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BancoCarrefour.ServiceDefaults.Authentication;

/// <summary>
/// Autenticação Bearer usando exclusivamente o handler padrão do ASP.NET Core (ADR-0006).
/// Nenhuma validação própria de token é implementada aqui; apenas a configuração restritiva do handler.
/// </summary>
public static class BearerAuthenticationExtensions
{
    /// <summary>Algoritmos assimétricos permitidos. <c>none</c> e HS* são rejeitados.</summary>
    public static readonly IReadOnlyList<string> AllowedAlgorithms =
    [
        SecurityAlgorithms.RsaSha256,
        SecurityAlgorithms.RsaSsaPssSha256,
        SecurityAlgorithms.EcdsaSha256,
    ];

    public static TBuilder AddDefaultBearerAuthentication<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<IValidateOptions<BearerAuthenticationOptions>, BearerAuthenticationOptionsValidator>();
        builder.Services
            .AddOptions<BearerAuthenticationOptions>()
            .Bind(builder.Configuration.GetSection(BearerAuthenticationOptions.SectionName))
            .ValidateOnStart();

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Ordem de execução (OptionsFactory: todos os IConfigureOptions, depois todos os IPostConfigureOptions,
        // cada grupo em ordem de registro):
        //   1. JwtBearerConfigureOptions (framework): binding de Authentication:Schemes:Bearer.
        //   2. ConfigureJwtBearer (abaixo): sobrescreve o que o binding pode ter alterado antes de o framework criar o
        //      ConfigurationManager (Authority, MetadataAddress, intervalos de refresh, HTTPS).
        //   3. JwtBearerPostConfigureOptions (framework): cria Backchannel e ConfigurationManager.
        //   4. PostConfigureJwtBearer (abaixo): substitui TokenValidationParameters inteiro (TA-019) e desliga o
        //      last-known-good do ConfigurationManager (TA-015).
        // Registrados após AddJwtBearer, que registra 1 e 3 via TryAddEnumerable.
        builder.Services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<BearerAuthenticationOptions>, IHostEnvironment>(ConfigureJwtBearer)
            .PostConfigure<IOptions<BearerAuthenticationOptions>>(PostConfigureJwtBearer);

        builder.Services
            .AddAuthorizationBuilder()
            .SetDefaultPolicy(RequireAuthenticatedUser())
            .SetFallbackPolicy(RequireAuthenticatedUser());

        return builder;
    }

    private static AuthorizationPolicy RequireAuthenticatedUser() =>
        new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build();

    private static void ConfigureJwtBearer(
        JwtBearerOptions jwt,
        IOptions<BearerAuthenticationOptions> bearerOptions,
        IHostEnvironment environment)
    {
        var settings = bearerOptions.Value;

        jwt.Authority = settings.Authority?.ToString();
        jwt.Audience = settings.Audience;
        jwt.RequireHttpsMetadata = !(environment.IsDevelopment() && settings.AllowInsecureMetadataInDevelopment);
        jwt.MapInboundClaims = false;
        jwt.SaveToken = false;
        jwt.IncludeErrorDetails = false;

        // Discovery sempre derivado da Authority validada: um MetadataAddress vindo do binding de
        // Authentication:Schemes:Bearer apontaria issuer e JWKS para outro endpoint sem passar pelo validador.
        jwt.MetadataAddress = string.Empty;

        // Sem encaminhamento para outro esquema por configuração (o binding aceita Forward*).
        jwt.ForwardDefault = null;
        jwt.ForwardAuthenticate = null;
        jwt.ForwardChallenge = null;
        jwt.ForwardForbid = null;
        jwt.ForwardSignIn = null;
        jwt.ForwardSignOut = null;
        jwt.Challenge = JwtBearerDefaults.AuthenticationScheme;

        // Timeouts e refresh explícitos do JWKS/discovery (fail-closed sem metadata válida).
        jwt.BackchannelTimeout = TimeSpan.FromSeconds(10);
        jwt.AutomaticRefreshInterval = JwksAutomaticRefreshInterval;
        jwt.RefreshInterval = JwksForcedRefreshMinInterval;
        jwt.RefreshOnIssuerKeyNotFound = true;
    }

    /// <summary>
    /// Refresh automático de discovery+JWKS (DEV-023). Com o last-known-good desligado, este intervalo limita a
    /// latência de revogação de uma chave removida do JWKS: o refresh roda em background, disparado pela primeira
    /// requisição após o vencimento, então a chave removida deixa de ser aceita em aproximadamente
    /// intervalo (+ jitter de até 5% do IdentityModel) + tempo até a próxima requisição + duração do fetch.
    /// 5 min é o mínimo aceito pelo IdentityModel (<see cref="BaseConfigurationManager.MinimumAutomaticRefreshInterval"/>)
    /// e é coerente com o TTL de 5–10 min do access token (ADR-0006) e com SEC-REQ-005.
    /// </summary>
    private static readonly TimeSpan JwksAutomaticRefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Intervalo mínimo entre refreshes forçados por kid desconhecido (RefreshOnIssuerKeyNotFound), que protege o
    /// IdP contra tokens forjados com kid aleatório. Mantido em 5 min (default do IdentityModel).
    /// </summary>
    private static readonly TimeSpan JwksForcedRefreshMinInterval = TimeSpan.FromMinutes(5);

    private static void PostConfigureJwtBearer(JwtBearerOptions jwt, IOptions<BearerAuthenticationOptions> bearerOptions)
    {
        var settings = bearerOptions.Value;

        // TA-019: objeto novo, construído só a partir das opções validadas. Descarta tudo o que o binding de
        // Authentication:Schemes:Bearer colocou em TokenValidationParameters (ValidAudiences, ValidIssuers,
        // ValidIssuer, ValidAudience, IssuerSigningKeys de SigningKeys, ValidateIssuer/ValidateAudience).
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [.. AllowedAlgorithms],
            ValidAudience = settings.Audience,
            ValidAudiences = null,
            ValidIssuers = null,
            IssuerSigningKey = null,
            IssuerSigningKeys = null,
            ClockSkew = TimeSpan.FromSeconds(
                Math.Clamp(settings.ClockSkewSeconds, 0, BearerAuthenticationOptions.MaxClockSkewSeconds)),
        };

        if (!string.IsNullOrWhiteSpace(settings.Issuer))
        {
            parameters.ValidIssuer = settings.Issuer;
        }

        jwt.TokenValidationParameters = parameters;

        // TA-015 / SEC-REQ-005: sem last-known-good. Com LKG ligado (padrão do IdentityModel, 1 h), um token
        // assinado por chave já removida do JWKS continua válido contra configurações anteriores em cache.
        // Falha de refresh durante indisponibilidade do IdP NÃO é afetada: o ConfigurationManager mantém a
        // configuração corrente quando o refresh em background falha; sem nenhuma configuração (cold start) a
        // validação falha (401).
        if (jwt.ConfigurationManager is not BaseConfigurationManager configurationManager)
        {
            throw new InvalidOperationException(
                "JwtBearer sem BaseConfigurationManager: não é possível desligar o last-known-good (fail-closed).");
        }

        configurationManager.UseLastKnownGoodConfiguration = false;
    }
}
