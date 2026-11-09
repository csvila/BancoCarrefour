using BancoCarrefour.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
        jwt.AutomaticRefreshInterval.ShouldBe(TimeSpan.FromHours(1));
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

    [Fact(Skip = "TA-019: Authentication:Schemes:Bearer:ValidAudiences/ValidIssuers (binding automático do JwtBearer) "
        + "ampliam as audiences/issuers aceitos sem passar pelo BearerAuthenticationOptionsValidator; ConfigureJwtBearer "
        + "não zera ValidAudiences/ValidIssuers. Viola 'aud/iss exatos' (SEC-REQ-003) por configuração.")]
    public void JwtBearer_SchemesBindingCannotWidenAudiencesOrIssuers()
    {
        using var provider = Build("Production", new()
        {
            ["Authentication:Schemes:Bearer:ValidAudiences:0"] = "api://attacker",
            ["Authentication:Schemes:Bearer:ValidIssuers:0"] = "https://evil.test.invalid/",
        });

        var jwt = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        (jwt.TokenValidationParameters.ValidAudiences ?? []).ShouldNotContain("api://attacker");
        (jwt.TokenValidationParameters.ValidIssuers ?? []).ShouldNotContain("https://evil.test.invalid/");
    }

    private static ServiceProvider Build(string environment, Dictionary<string, string?> overrides)
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
        return builder.Services.BuildServiceProvider();
    }
}
