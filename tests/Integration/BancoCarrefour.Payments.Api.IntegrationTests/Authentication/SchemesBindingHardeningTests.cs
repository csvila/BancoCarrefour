using System.Net;
using System.Security.Cryptography;
using BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure;
using BancoCarrefour.TestKit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using static BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure.HttpAssertions;

namespace BancoCarrefour.Payments.Api.IntegrationTests.Authentication;

/// <summary>
/// DEV-022, TA-019 e TA-015 ponta a ponta: nada em <c>Authentication:Schemes:Bearer</c> (binding automático do
/// JwtBearer) pode ampliar ou desviar a validação do Payments API real. Cada teste tem um controle positivo:
/// <list type="bullet">
/// <item>o token legítimo continua aceito (204) no mesmo host, provando que o 401 não vem de um host quebrado;</item>
/// <item>um esquema JwtBearer de controle (<see cref="ControlScheme"/>), registrado só no teste e sem o endurecimento
/// do ServiceDefaults, recebe os mesmos valores hostis e os honra, provando que o vetor de ataque existe.</item>
/// </list>
/// </summary>
public sealed class SchemesBindingHardeningTests
{
    private const string ControlScheme = "ControlJwt";
    private const string EvilIssuer = "https://evil.test.invalid/";
    private const string EvilAudience = "api://injected-audience";
    private const string EvilMetadata = "https://evil.test.invalid/.well-known/openid-configuration";
    private const string InjectedKid = "injected-oct";

    // Segredo sintético gerado por execução; nunca persistido.
    private static readonly byte[] InjectedSecret = RandomNumberGenerator.GetBytes(64);

    [Theory]
    [InlineData("ForwardDefault")]
    [InlineData("ForwardAuthenticate")]
    [InlineData("ForwardChallenge")]
    public async Task Ping_ForwardInjectedViaSchemesBinding_DoesNotBypassBearer(string forwardKey)
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            [$"Authentication:Schemes:Bearer:{forwardKey}"] = AlwaysAllowAuthenticationHandler.SchemeName,
            [$"Authentication:Schemes:{ControlScheme}:{forwardKey}"] = AlwaysAllowAuthenticationHandler.SchemeName,
        });
        using var client = factory.CreateClient();
        using var attackerKey = RSA.Create(2048);

        // Controle positivo do vetor: o esquema de controle encaminha de fato para o esquema atacante.
        if (forwardKey == "ForwardChallenge")
        {
            (await ChallengeStatusAsync(factory, ControlScheme)).ShouldBe(AlwaysAllowAuthenticationHandler.ChallengeStatus);
        }
        else
        {
            (await AuthenticateAsync(factory, ControlScheme)).Succeeded.ShouldBeTrue();
        }

        (await PingStatusAsync(client, factory.Tokens.Create())).ShouldBe(HttpStatusCode.NoContent);

        using var anonymous = await SendAsync(client, Get(PingPath));
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        anonymous.Headers.WwwAuthenticate.ShouldContain(h => h.Scheme == JwtBearerDefaults.AuthenticationScheme);
        (await PingStatusAsync(client, factory.Tokens.Create(s => s.RsaKeyOverride = attackerKey)))
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await AuthenticateAsync(factory, JwtBearerDefaults.AuthenticationScheme)).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Ping_ChallengeSchemeInjectedViaSchemesBinding_WwwAuthenticateStaysBearer()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Authentication:Schemes:Bearer:Challenge"] = "Evil",
            [$"Authentication:Schemes:{ControlScheme}:Challenge"] = "Evil",
        });
        using var client = factory.CreateClient();

        // Controle: no esquema não endurecido, o binding troca o valor do WWW-Authenticate.
        var controlContext = await ChallengeAsync(factory, ControlScheme);
        controlContext.Response.Headers.WWWAuthenticate.ToString().ShouldStartWith("Evil");

        using var response = await SendAsync(client, Get(PingPath));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ShouldContain(h => h.Scheme == JwtBearerDefaults.AuthenticationScheme);
        response.Headers.WwwAuthenticate.ShouldNotContain(h => h.Scheme == "Evil");
    }

    [Fact]
    public async Task Ping_MetadataAddressInjectedViaSchemesBinding_DiscoveryStillFromValidatedAuthority()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Authentication:Schemes:Bearer:MetadataAddress"] = EvilMetadata,
            [$"Authentication:Schemes:{ControlScheme}:MetadataAddress"] = EvilMetadata,
        });
        using var client = factory.CreateClient();
        using var attackerKey = RSA.Create(2048);
        var monitor = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();

        // Controle: o esquema não endurecido apontaria o discovery para o endpoint injetado.
        monitor.Get(ControlScheme).ConfigurationManager.ShouldBeAssignableTo<BaseConfigurationManager>()
            .ShouldNotBeNull().MetadataAddress.ShouldBe(EvilMetadata);

        // Comportamento: o token legítimo só é aceito se discovery e JWKS vierem da Authority validada (o handler
        // fake devolve 404 e registra qualquer requisição para outro host).
        (await PingStatusAsync(client, factory.Tokens.Create())).ShouldBe(HttpStatusCode.NoContent);
        (await PingStatusAsync(client, factory.Tokens.Create(s => s.RsaKeyOverride = attackerKey)))
            .ShouldBe(HttpStatusCode.Unauthorized);
        factory.Idp.DiscoveryRequests.ShouldBeGreaterThanOrEqualTo(1);
        factory.Idp.JwksRequests.ShouldBeGreaterThanOrEqualTo(1);
        factory.Idp.UnexpectedRequests.ShouldBeEmpty();

        var manager = monitor.Get(JwtBearerDefaults.AuthenticationScheme).ConfigurationManager
            .ShouldBeAssignableTo<BaseConfigurationManager>().ShouldNotBeNull();
        manager.MetadataAddress.ShouldBe(FakeIdentityProvider.Authority + ".well-known/openid-configuration");
    }

    [Fact]
    public async Task Ping_IssuersAudiencesAndSymmetricKeyInjectedViaSchemesBinding_Return401()
    {
        var hostile = new Dictionary<string, string?>
        {
            ["ValidIssuer"] = EvilIssuer,
            ["ValidIssuers:0"] = EvilIssuer,
            ["ValidAudience"] = EvilAudience,
            ["ValidAudiences:0"] = EvilAudience,

            // O framework só transforma SigningKeys em IssuerSigningKeys para issuers da lista acima.
            ["SigningKeys:0:Issuer"] = FakeIdentityProvider.Issuer,
            ["SigningKeys:0:Value"] = Convert.ToBase64String(InjectedSecret),
            ["SigningKeys:1:Issuer"] = EvilIssuer,
            ["SigningKeys:1:Value"] = Convert.ToBase64String(InjectedSecret),
            ["ValidIssuers:1"] = FakeIdentityProvider.Issuer,
        };
        var settings = new Dictionary<string, string?>();
        foreach (var (key, value) in hostile)
        {
            settings[$"Authentication:Schemes:Bearer:{key}"] = value;
            settings[$"Authentication:Schemes:{ControlScheme}:{key}"] = value;
        }

        await using var factory = CreateFactory(settings);
        using var client = factory.CreateClient();
        var monitor = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();

        // Controle: no esquema não endurecido o binding amplia issuers/audiences e injeta a chave simétrica.
        var control = monitor.Get(ControlScheme).TokenValidationParameters;
        control.ValidIssuers.ShouldNotBeNull().ShouldContain(EvilIssuer);
        control.ValidAudiences.ShouldNotBeNull().ShouldContain(EvilAudience);
        control.IssuerSigningKeys.ShouldNotBeNull().ShouldContain(k => k is SymmetricSecurityKey);

        (await PingStatusAsync(client, factory.Tokens.Create())).ShouldBe(HttpStatusCode.NoContent);

        (await PingStatusAsync(client, factory.Tokens.Create(s => s.Issuer = EvilIssuer))).ShouldBe(HttpStatusCode.Unauthorized);
        (await PingStatusAsync(client, factory.Tokens.Create(s => s.Audience = EvilAudience))).ShouldBe(HttpStatusCode.Unauthorized);
        (await PingStatusAsync(client, factory.Tokens.Create(s =>
        {
            s.Alg = "HS256";
            s.Kid = InjectedKid;
            s.HmacSecret = InjectedSecret;
        }))).ShouldBe(HttpStatusCode.Unauthorized);
        (await PingStatusAsync(client, factory.Tokens.Create(s =>
        {
            s.Alg = "HS256";
            s.Issuer = EvilIssuer;
            s.Kid = InjectedKid;
            s.HmacSecret = InjectedSecret;
        }))).ShouldBe(HttpStatusCode.Unauthorized);

        var parameters = monitor.Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        parameters.ValidIssuers.ShouldBeNull();
        parameters.ValidAudiences.ShouldBeNull();
        parameters.IssuerSigningKey.ShouldBeNull();
        parameters.IssuerSigningKeys.ShouldBeNull();
    }

    [Fact]
    public async Task JwtBearer_ResolvedConfigurationManager_HasLastKnownGoodDisabled()
    {
        // TA-015 (configuração no host real). Controle: o JwtBearer padrão do framework vem com LKG ligado; o
        // comportamento (chave removida -> 401) é coberto por JwksResolutionAndFailClosedTests.
        await using var factory = CreateFactory([]);
        using var client = factory.CreateClient();
        (await PingStatusAsync(client, factory.Tokens.Create())).ShouldBe(HttpStatusCode.NoContent);
        var monitor = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();

        monitor.Get(ControlScheme).ConfigurationManager.ShouldBeAssignableTo<BaseConfigurationManager>()
            .ShouldNotBeNull().UseLastKnownGoodConfiguration.ShouldBeTrue();
        monitor.Get(JwtBearerDefaults.AuthenticationScheme).ConfigurationManager.ShouldBeAssignableTo<BaseConfigurationManager>()
            .ShouldNotBeNull().UseLastKnownGoodConfiguration.ShouldBeFalse();
    }

    private static PaymentsApiFactory CreateFactory(Dictionary<string, string?> settings)
    {
        settings[$"Authentication:Schemes:{ControlScheme}:Authority"] = FakeIdentityProvider.Authority;
        return new PaymentsApiFactory(settingsOverride: settings, configureServices: services =>
            services.AddAuthentication()
                .AddJwtBearer(ControlScheme)
                .AddScheme<AuthenticationSchemeOptions, AlwaysAllowAuthenticationHandler>(
                    AlwaysAllowAuthenticationHandler.SchemeName, configureOptions: null));
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(PaymentsApiFactory factory, string scheme)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        return await context.AuthenticateAsync(scheme);
    }

    private static async Task<HttpContext> ChallengeAsync(PaymentsApiFactory factory, string scheme)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        await context.ChallengeAsync(scheme);
        return context;
    }

    private static async Task<int> ChallengeStatusAsync(PaymentsApiFactory factory, string scheme) =>
        (await ChallengeAsync(factory, scheme)).Response.StatusCode;
}
