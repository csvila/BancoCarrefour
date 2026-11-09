using BancoCarrefour.TestKit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Hospeda a Payments API real (Program.cs de produção) com o IdP fake.
/// <para>
/// Escolha de integração do IdP (opção "b" da delegação): a Authority continua HTTPS e o handler JwtBearer de
/// produção busca discovery + JWKS normalmente; o teste só substitui o transporte do backchannel
/// (<see cref="JwtBearerOptions.BackchannelHttpHandler"/>) via <c>Configure</c>, que roda ANTES do
/// <c>JwtBearerPostConfigureOptions</c> criar o <c>ConfigurationManager</c>. Não são tocados ValidAlgorithms,
/// ValidateX, ClockSkew, RequireHttpsMetadata, ValidIssuer/ValidAudience nem chaves: tudo vem do código de produção
/// e da configuração (Authority/Audience) que um ambiente real também forneceria.
/// </para>
/// </summary>
public sealed class PaymentsApiFactory : WebApplicationFactory<Program>
{
    public const string Audience = "api://payments-test";

    private readonly Dictionary<string, string?> _settings;

    public PaymentsApiFactory(
        string environment = "Production",
        FakeIdentityProvider? idp = null,
        IDictionary<string, string?>? settingsOverride = null,
        Action<IServiceCollection>? configureServices = null)
    {
        Environment = environment;
        Idp = idp ?? new FakeIdentityProvider();
        Tokens = new JwtFactory(Idp, Audience);
        ConfigureServicesHook = configureServices;
        _settings = new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = FakeIdentityProvider.Authority,
            ["Authentication:Audience"] = Audience,
            ["Authentication:ClockSkewSeconds"] = "30",
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

    public FakeIdentityProvider Idp { get; }

    public JwtFactory Tokens { get; }

    public LogCapture Logs { get; } = new();

    private Action<IServiceCollection>? ConfigureServicesHook { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environment);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(Logs);
            services.Configure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options => options.BackchannelHttpHandler = Idp.CreateHandler());
            ConfigureServicesHook?.Invoke(services);
        });
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
