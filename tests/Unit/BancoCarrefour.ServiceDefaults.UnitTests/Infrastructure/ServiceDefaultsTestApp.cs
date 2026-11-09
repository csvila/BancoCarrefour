using BancoCarrefour.ServiceDefaults.Correlation;
using BancoCarrefour.TestKit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BancoCarrefour.ServiceDefaults.UnitTests.Infrastructure;

/// <summary>
/// App mínima em memória que aplica apenas <c>AddServiceDefaults</c>/<c>UseServiceDefaults</c> (API pública) e mapeia
/// endpoints anônimos de sonda. Os middlewares do ServiceDefaults são internos; esta é a superfície estável para
/// testá-los isoladamente, sem depender de nomes de tipos internos (ver pedido de InternalsVisibleTo no relatório).
/// Nenhuma requisição exige token, então o IdP nunca é contatado.
/// </summary>
public sealed class ServiceDefaultsTestApp : IAsyncDisposable
{
    public const string CorrelationPath = "/probe/correlation";
    public const string OkPath = "/probe/ok";

    private readonly WebApplication _app;

    private ServiceDefaultsTestApp(WebApplication app, LogCapture logs)
    {
        _app = app;
        Logs = logs;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public LogCapture Logs { get; }

    public static async Task<ServiceDefaultsTestApp> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = "https://idp.test.invalid/",
            ["Authentication:Audience"] = "api://unit-test",
        });
        var logs = new LogCapture();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.AddServiceDefaults();

        var app = builder.Build();
        app.UseServiceDefaults();
        app.MapGet(CorrelationPath, static (HttpContext context) => context.GetCorrelationId() ?? "<none>").AllowAnonymous();
        app.MapGet(OkPath, static () => "ok").AllowAnonymous();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return new ServiceDefaultsTestApp(app, logs);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }

    public Task<HttpContext> SendRawAsync(Action<HttpContext> configure) =>
        _app.GetTestServer().SendAsync(configure, TestContext.Current.CancellationToken);
}
