using BancoCarrefour.TestKit;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BancoCarrefour.CoreBanking.Host.IntegrationTests.Infrastructure;

/// <summary>
/// Hospeda o CoreBanking.Host real com IdP fake (mesma estratégia da Payments API: só o transporte do backchannel
/// do JwtBearer é substituído; nenhuma regra de validação é alterada).
/// </summary>
public sealed class CoreBankingHostFactory : WebApplicationFactory<Program>
{
    public const string Audience = "api://corebanking-test";

    private readonly Dictionary<string, string?> _settings;

    public CoreBankingHostFactory(string environment = "Production", IDictionary<string, string?>? settingsOverride = null)
    {
        Environment = environment;
        Tokens = new JwtFactory(Idp, Audience);
        _settings = new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = FakeIdentityProvider.Authority,
            ["Authentication:Audience"] = Audience,
        };
        if (settingsOverride is not null)
        {
            foreach (var (key, value) in settingsOverride)
            {
                _settings[key] = value;
            }
        }
    }

    public string Environment { get; }

    public FakeIdentityProvider Idp { get; } = new();

    public JwtFactory Tokens { get; }

    public GrpcChannel CreateGrpcChannel() =>
        GrpcChannel.ForAddress(Server.BaseAddress, new GrpcChannelOptions { HttpHandler = Server.CreateHandler() });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environment);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
            services.Configure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options => options.BackchannelHttpHandler = Idp.CreateHandler()));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Idp.Dispose();
        }
    }
}
