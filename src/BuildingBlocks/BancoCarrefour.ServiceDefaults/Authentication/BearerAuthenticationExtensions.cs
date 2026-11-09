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

        // Registrado após AddJwtBearer: prevalece sobre o binding automático de Authentication:Schemes:Bearer.
        builder.Services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<BearerAuthenticationOptions>, IHostEnvironment>(ConfigureJwtBearer);

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

        // Timeouts e refresh explícitos do JWKS/discovery (fail-closed sem metadata válida).
        jwt.BackchannelTimeout = TimeSpan.FromSeconds(10);
        jwt.AutomaticRefreshInterval = TimeSpan.FromHours(1);
        jwt.RefreshInterval = TimeSpan.FromMinutes(5);
        jwt.RefreshOnIssuerKeyNotFound = true;

        var parameters = jwt.TokenValidationParameters;
        parameters.ValidateIssuer = true;
        parameters.ValidateAudience = true;
        parameters.ValidateLifetime = true;
        parameters.ValidateIssuerSigningKey = true;
        parameters.RequireExpirationTime = true;
        parameters.RequireSignedTokens = true;
        parameters.ValidAlgorithms = [.. AllowedAlgorithms];
        parameters.ValidAudience = settings.Audience;
        parameters.ClockSkew = TimeSpan.FromSeconds(
            Math.Clamp(settings.ClockSkewSeconds, 0, BearerAuthenticationOptions.MaxClockSkewSeconds));

        if (!string.IsNullOrWhiteSpace(settings.Issuer))
        {
            parameters.ValidIssuer = settings.Issuer;
        }
    }
}
