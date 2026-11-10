using BancoCarrefour.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BancoCarrefour.ServiceDefaults.UnitTests.Authentication;

/// <summary>
/// Validação fail-closed das opções Bearer e configuração restritiva do JwtBearer (ADR-0006; SEC-REQ-001/004;
/// TC-218). O validador é interno: é exercitado pela API pública <c>AddDefaultBearerAuthentication</c> + DI.
/// </summary>
public sealed class BearerAuthenticationConfigurationTests
{
    private const string ValidAuthority = "https://idp.test.invalid/";
    private const string ValidAudience = "api://payments-test";
    private const string ControlScheme = "Control";
    private const string EvilScheme = "Evil";
    private const string EvilIssuer = "https://evil.test.invalid/";
    private const string EvilAudience = "api://attacker";
    private const string EvilMetadata = "https://evil.test.invalid/.well-known/openid-configuration";

    public static TheoryData<string, string, string?, string> InvalidSettings() => new()
    {
        { "Production", "Authority", null, "Authentication:Authority" },
        { "Production", "Authority", "idp.test.invalid", "Authentication:Authority" },
        { "Production", "Authority", "/relative/path", "Authentication:Authority" },
        { "Production", "Authority", "http://idp.test.invalid/", "HTTPS" },
        { "Staging", "Authority", "http://idp.test.invalid/", "HTTPS" },
        { "Development", "Authority", "http://idp.test.invalid/", "HTTPS" },
        { "Production", "Audience", null, "Authentication:Audience" },
        { "Production", "Audience", "", "Authentication:Audience" },
        { "Production", "Audience", "   ", "Authentication:Audience" },
        { "Production", "ClockSkewSeconds", "-1", "ClockSkewSeconds" },
        { "Production", "ClockSkewSeconds", "61", "ClockSkewSeconds" },
        { "Production", "ClockSkewSeconds", "3600", "ClockSkewSeconds" },
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public void Options_InvalidSetting_FailsValidation(string environment, string key, string? value, string expectedFragment)
    {
        using var provider = Build(environment, new() { [$"Authentication:{key}"] = value });

        var exception = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<BearerAuthenticationOptions>>().Value);

        exception.Failures.ShouldContain(f => f.Contains(expectedFragment, StringComparison.Ordinal));
    }

    [Fact]
    public void Options_HttpAuthorityWithInsecureFlagOutsideDevelopment_FailsValidation()
    {
        using var provider = Build("Production", new()
        {
            ["Authentication:Authority"] = "http://idp.test.invalid/",
            ["Authentication:AllowInsecureMetadataInDevelopment"] = "true",
        });

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<BearerAuthenticationOptions>>().Value);
    }

    [Fact]
    public void Options_HttpAuthorityWithInsecureFlagInDevelopment_IsAccepted()
    {
        using var provider = Build("Development", new()
        {
            ["Authentication:Authority"] = "http://localhost:8080/realms/dev/",
            ["Authentication:AllowInsecureMetadataInDevelopment"] = "true",
        });

        Should.NotThrow(() => provider.GetRequiredService<IOptions<BearerAuthenticationOptions>>().Value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("30")]
    [InlineData("60")]
    public void Options_ClockSkewWithinRange_IsAccepted(string skew)
    {
        using var provider = Build("Production", new() { ["Authentication:ClockSkewSeconds"] = skew });

        provider.GetRequiredService<IOptions<BearerAuthenticationOptions>>().Value.ClockSkewSeconds.ShouldBe(int.Parse(skew, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Options_MissingAuthorityAndAudience_ReportsAllFailuresTogether()
    {
        using var provider = Build("Production", new()
        {
            ["Authentication:Authority"] = null,
            ["Authentication:Audience"] = null,
            ["Authentication:ClockSkewSeconds"] = "99",
        });

        var exception = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<BearerAuthenticationOptions>>().Value);

        exception.Failures.Count().ShouldBe(3);
    }

    [Fact]
    public void JwtBearer_IsConfiguredRestrictively()
    {
        using var provider = Build("Production", new() { ["Authentication:ClockSkewSeconds"] = "45" });

        var jwt = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        var parameters = jwt.TokenValidationParameters;

        parameters.ValidAlgorithms.ShouldBe(
            [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.EcdsaSha256], ignoreOrder: true);
        parameters.ValidateIssuer.ShouldBeTrue();
        parameters.ValidateAudience.ShouldBeTrue();
        parameters.ValidateLifetime.ShouldBeTrue();
        parameters.ValidateIssuerSigningKey.ShouldBeTrue();
        parameters.RequireExpirationTime.ShouldBeTrue();
        parameters.RequireSignedTokens.ShouldBeTrue();
        parameters.ValidAudience.ShouldBe(ValidAudience);
        parameters.ClockSkew.ShouldBe(TimeSpan.FromSeconds(45));
        jwt.Authority.ShouldBe(ValidAuthority);
        jwt.RequireHttpsMetadata.ShouldBeTrue();
        jwt.MapInboundClaims.ShouldBeFalse();
        jwt.SaveToken.ShouldBeFalse();
        jwt.IncludeErrorDetails.ShouldBeFalse();
        jwt.RefreshOnIssuerKeyNotFound.ShouldBeTrue();
        jwt.BackchannelTimeout.ShouldBe(TimeSpan.FromSeconds(10));
        jwt.RefreshInterval.ShouldBe(TimeSpan.FromMinutes(5));
        // DEV-023 (autorizado pelo usuário): 5 min, limita a latência de revogação de chave removida do JWKS.
        jwt.AutomaticRefreshInterval.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Theory]
    [InlineData("none")]
    [InlineData(SecurityAlgorithms.HmacSha256)]
    [InlineData(SecurityAlgorithms.HmacSha512)]
    [InlineData(SecurityAlgorithms.RsaSha384)]
    [InlineData(SecurityAlgorithms.EcdsaSha384)]
    public void AllowedAlgorithms_DoNotContainForbiddenAlgorithm(string algorithm) =>
        BearerAuthenticationExtensions.AllowedAlgorithms.ShouldNotContain(algorithm);

    [Theory]
    [InlineData("Production", "true", true)]
    [InlineData("Staging", "true", true)]
    [InlineData("Development", "false", true)]
    [InlineData("Development", "true", false)]
    public void JwtBearer_RequireHttpsMetadata_OnlyRelaxedInDevelopmentWithOptIn(string environment, string allowInsecure, bool expected)
    {
        using var provider = Build(environment, new() { ["Authentication:AllowInsecureMetadataInDevelopment"] = allowInsecure });

        var jwt = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        jwt.RequireHttpsMetadata.ShouldBe(expected);
    }

    [Fact]
    public void JwtBearer_ExplicitIssuer_IsUsedAsValidIssuer()
    {
        using var provider = Build("Production", new() { ["Authentication:Issuer"] = "https://idp.test.invalid/realms/x" });

        var jwt = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        jwt.TokenValidationParameters.ValidIssuer.ShouldBe("https://idp.test.invalid/realms/x");
    }

    [Fact]
    public void JwtBearer_SchemesBindingCannotOverrideAuthorityOrHttpsMetadata()
    {
        // O binding automático de Authentication:Schemes:Bearer não pode relaxar a configuração de produção.
        using var provider = Build("Production", new()
        {
            ["Authentication:Schemes:Bearer:RequireHttpsMetadata"] = "false",
            ["Authentication:Schemes:Bearer:Authority"] = "https://evil.test.invalid/",
            ["Authentication:Schemes:Bearer:Audience"] = "api://attacker",
        });

        var jwt = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        jwt.RequireHttpsMetadata.ShouldBeTrue();
        jwt.Authority.ShouldBe(ValidAuthority);
        jwt.Audience.ShouldBe(ValidAudience);
        jwt.TokenValidationParameters.ValidAudience.ShouldBe(ValidAudience);
    }

    [Fact]
    public void JwtBearer_SchemesBindingCannotWidenAudiencesOrIssuers()
    {
        // Gate de TA-019.
        using var provider = Build("Production", new()
        {
            ["Authentication:Schemes:Bearer:ValidAudiences:0"] = "api://attacker",
            ["Authentication:Schemes:Bearer:ValidIssuers:0"] = "https://evil.test.invalid/",
        });

        var jwt = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        (jwt.TokenValidationParameters.ValidAudiences ?? []).ShouldNotContain("api://attacker");
        (jwt.TokenValidationParameters.ValidIssuers ?? []).ShouldNotContain("https://evil.test.invalid/");
    }

    [Fact]
    public void JwtBearer_SchemesBindingCannotRedirectMetadataForwardOrInjectKeys()
    {
        // DEV-022 + TA-019. Os mesmos valores hostis são aplicados a um esquema JwtBearer de controle ("Control"),
        // sem o endurecimento do ServiceDefaults: o controle prova que o binding do framework realmente lê cada
        // chave (sem ele, as asserções sobre "Bearer" poderiam ser vacuamente verdadeiras).
        var overrides = new Dictionary<string, string?>();
        foreach (var (key, value) in HostileSchemeSettings())
        {
            overrides[$"Authentication:Schemes:Bearer:{key}"] = value;
            overrides[$"Authentication:Schemes:{ControlScheme}:{key}"] = value;
        }

        overrides[$"Authentication:Schemes:{ControlScheme}:Authority"] = ValidAuthority;
        using var provider = Build("Production", overrides, AddControlScheme);
        var monitor = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();

        var control = monitor.Get(ControlScheme);
        control.MetadataAddress.ShouldBe(EvilMetadata);
        control.ForwardDefault.ShouldBe(EvilScheme);
        control.ForwardAuthenticate.ShouldBe(EvilScheme);
        control.ForwardChallenge.ShouldBe(EvilScheme);
        control.ForwardForbid.ShouldBe(EvilScheme);
        control.ForwardSignIn.ShouldBe(EvilScheme);
        control.ForwardSignOut.ShouldBe(EvilScheme);
        control.Challenge.ShouldBe(EvilScheme);
        control.RefreshInterval.ShouldBe(TimeSpan.FromSeconds(1));
        control.BackchannelTimeout.ShouldBe(TimeSpan.FromMinutes(10));
        control.TokenValidationParameters.ValidIssuers.ShouldNotBeNull().ShouldContain(EvilIssuer);
        control.TokenValidationParameters.ValidAudiences.ShouldNotBeNull().ShouldContain(EvilAudience);
        control.TokenValidationParameters.IssuerSigningKeys.ShouldNotBeNull().ShouldContain(k => k is SymmetricSecurityKey);

        var jwt = monitor.Get(JwtBearerDefaults.AuthenticationScheme);
        var parameters = jwt.TokenValidationParameters;
        // JwtBearerPostConfigureOptions deriva o MetadataAddress da Authority quando ele chega vazio.
        jwt.MetadataAddress.ShouldBe(ValidAuthority + ".well-known/openid-configuration");
        jwt.Authority.ShouldBe(ValidAuthority);
        jwt.ForwardDefault.ShouldBeNull();
        jwt.ForwardAuthenticate.ShouldBeNull();
        jwt.ForwardChallenge.ShouldBeNull();
        jwt.ForwardForbid.ShouldBeNull();
        jwt.ForwardSignIn.ShouldBeNull();
        jwt.ForwardSignOut.ShouldBeNull();
        jwt.ForwardDefaultSelector.ShouldBeNull();
        jwt.Challenge.ShouldBe(JwtBearerDefaults.AuthenticationScheme);
        parameters.ValidIssuer.ShouldBeNull();
        parameters.ValidIssuers.ShouldBeNull();
        parameters.ValidAudience.ShouldBe(ValidAudience);
        parameters.ValidAudiences.ShouldBeNull();
        parameters.IssuerSigningKey.ShouldBeNull();
        parameters.IssuerSigningKeys.ShouldBeNull();
        parameters.ValidateIssuer.ShouldBeTrue();
        parameters.ValidateAudience.ShouldBeTrue();
        parameters.ValidAlgorithms.ShouldBe(BearerAuthenticationExtensions.AllowedAlgorithms, ignoreOrder: true);
        jwt.RequireHttpsMetadata.ShouldBeTrue();
        jwt.IncludeErrorDetails.ShouldBeFalse();
        jwt.SaveToken.ShouldBeFalse();
        jwt.MapInboundClaims.ShouldBeFalse();
        jwt.RefreshOnIssuerKeyNotFound.ShouldBeTrue();
        jwt.BackchannelTimeout.ShouldBe(TimeSpan.FromSeconds(10));
        jwt.RefreshInterval.ShouldBe(TimeSpan.FromMinutes(5));
        jwt.AutomaticRefreshInterval.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void JwtBearer_ConfigurationManager_HasLastKnownGoodDisabledAndUsesAuthorityDiscovery()
    {
        // TA-015 (configuração) + DEV-022. Controle: um esquema JwtBearer padrão do framework mantém o LKG ligado e
        // honra o MetadataAddress do binding; o esquema Bearer endurecido não.
        using var provider = Build("Production", new()
        {
            ["Authentication:Schemes:Bearer:MetadataAddress"] = EvilMetadata,
            [$"Authentication:Schemes:{ControlScheme}:Authority"] = ValidAuthority,
            [$"Authentication:Schemes:{ControlScheme}:MetadataAddress"] = EvilMetadata,
        }, AddControlScheme);
        var monitor = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();

        var control = monitor.Get(ControlScheme).ConfigurationManager.ShouldBeAssignableTo<BaseConfigurationManager>();
        control.ShouldNotBeNull().UseLastKnownGoodConfiguration.ShouldBeTrue();
        control.MetadataAddress.ShouldBe(EvilMetadata);

        var manager = monitor.Get(JwtBearerDefaults.AuthenticationScheme).ConfigurationManager.ShouldBeAssignableTo<BaseConfigurationManager>();
        manager.ShouldNotBeNull().UseLastKnownGoodConfiguration.ShouldBeFalse();
        manager.MetadataAddress.ShouldBe(ValidAuthority + ".well-known/openid-configuration");
    }

    [Fact]
    public void JwtBearer_ConfigurationManagerNotDerivedFromBase_FailsClosed()
    {
        // TA-015: sem BaseConfigurationManager não há como desligar o LKG; o PostConfigure precisa falhar em vez de
        // seguir com LKG em estado desconhecido.
        using var provider = Build("Production", [], builder => builder.Services.Configure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options => options.ConfigurationManager = new NonBaseConfigurationManager()));

        Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme));
    }

    private static Dictionary<string, string?> HostileSchemeSettings() => new()
    {
        ["MetadataAddress"] = EvilMetadata,
        ["ForwardDefault"] = EvilScheme,
        ["ForwardAuthenticate"] = EvilScheme,
        ["ForwardChallenge"] = EvilScheme,
        ["ForwardForbid"] = EvilScheme,
        ["ForwardSignIn"] = EvilScheme,
        ["ForwardSignOut"] = EvilScheme,
        ["Challenge"] = EvilScheme,
        ["ValidIssuer"] = EvilIssuer,
        ["ValidIssuers:0"] = EvilIssuer,
        ["ValidAudience"] = EvilAudience,
        ["ValidAudiences:0"] = EvilAudience,
        ["SigningKeys:0:Issuer"] = EvilIssuer,

        // Segredo sintético de teste (32 bytes 0x2A), sem uso fora deste teste.
        ["SigningKeys:0:Value"] = Convert.ToBase64String(Enumerable.Repeat((byte)0x2A, 32).ToArray()),
        ["RequireHttpsMetadata"] = "false",
        ["IncludeErrorDetails"] = "true",
        ["SaveToken"] = "true",
        ["MapInboundClaims"] = "true",
        ["RefreshOnIssuerKeyNotFound"] = "false",
        ["BackchannelTimeout"] = "00:10:00",
        ["RefreshInterval"] = "00:00:01",
        // Mantido como defesa em profundidade: o JwtBearerConfigureOptions do ASP.NET Core 10.0.14 NÃO lê esta chave
        // (o esquema de controle fica com o default de 12 h do IdentityModel), então não há controle positivo para ela.
        ["AutomaticRefreshInterval"] = "1.00:00:00",
    };

    private static void AddControlScheme(IHostApplicationBuilder builder) =>
        builder.Services.AddAuthentication().AddJwtBearer(ControlScheme);

    private static ServiceProvider Build(
        string environment,
        Dictionary<string, string?> overrides,
        Action<IHostApplicationBuilder>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment,
            DisableDefaults = true,
        });
        var settings = new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = ValidAuthority,
            ["Authentication:Audience"] = ValidAudience,
        };
        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddDefaultBearerAuthentication();
        configure?.Invoke(builder);
        return builder.Services.BuildServiceProvider();
    }

    /// <summary>ConfigurationManager que não deriva de <see cref="BaseConfigurationManager"/> (TA-015 fail-closed).</summary>
    private sealed class NonBaseConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
            throw new InvalidOperationException("Não deve ser chamado neste teste.");

        public void RequestRefresh()
        {
        }
    }
}
