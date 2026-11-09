using BancoCarrefour.ServiceDefaults.Correlation;
using BancoCarrefour.ServiceDefaults.UnitTests.Infrastructure;
using BancoCarrefour.TestKit;
using Microsoft.AspNetCore.Http;

namespace BancoCarrefour.ServiceDefaults.UnitTests.Middleware;

/// <summary>CorrelationIdMiddleware: gera, propaga para feature/Activity/header e rejeita valores inválidos (NFR-113).</summary>
public sealed class CorrelationIdMiddlewareTests : IAsyncLifetime
{
    private ServiceDefaultsTestApp _app = null!;

    public async ValueTask InitializeAsync() => _app = await ServiceDefaultsTestApp.StartAsync();

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Invoke_WithoutHeader_GeneratesGuidNAndExposesSameValueInFeatureAndResponse()
    {
        using var response = await _app.Client.GetAsync(ServiceDefaultsTestApp.CorrelationPath, TestContext.Current.CancellationToken);

        var fromFeature = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        fromFeature.ShouldMatch("^[0-9a-f]{32}$");
        response.Headers.GetValues(CorrelationIdFeature.HeaderName).Single().ShouldBe(fromFeature);
    }

    [Fact]
    public async Task Invoke_TwoRequestsWithoutHeader_GenerateDistinctIds()
    {
        using var first = await _app.Client.GetAsync(ServiceDefaultsTestApp.CorrelationPath, TestContext.Current.CancellationToken);
        using var second = await _app.Client.GetAsync(ServiceDefaultsTestApp.CorrelationPath, TestContext.Current.CancellationToken);

        (await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldNotBe(await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ABC-def_123.456:789")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public async Task Invoke_WithValidHeader_PropagatesValue(string correlationId)
    {
        var context = await _app.SendRawAsync(c =>
        {
            c.Request.Path = ServiceDefaultsTestApp.CorrelationPath;
            c.Request.Headers[CorrelationIdFeature.HeaderName] = correlationId;
        });

        (await ReadBodyAsync(context)).ShouldBe(correlationId);
        context.Response.Headers[CorrelationIdFeature.HeaderName].ToString().ShouldBe(correlationId);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0")]
    [InlineData("with space")]
    [InlineData("crlf\r\nSet-Cookie: x=1")]
    [InlineData("semi;colon")]
    [InlineData("slash/value")]
    [InlineData("percent%0A")]
    [InlineData("unicode-ç")]
    [InlineData("\u0000")]
    public async Task Invoke_WithInvalidHeader_ReplacesWithGeneratedId(string invalid)
    {
        var context = await _app.SendRawAsync(c =>
        {
            c.Request.Path = ServiceDefaultsTestApp.CorrelationPath;
            c.Request.Headers[CorrelationIdFeature.HeaderName] = invalid;
        });

        var effective = await ReadBodyAsync(context);
        effective.ShouldMatch("^[0-9a-f]{32}$");
        context.Response.Headers[CorrelationIdFeature.HeaderName].ToString().ShouldBe(effective);
    }

    [Fact]
    public async Task Invoke_SetsActivityTagWithCorrelationId()
    {
        using var activities = new ActivityCapture();
        var correlationId = $"act-{Guid.NewGuid():N}";

        await _app.SendRawAsync(c =>
        {
            c.Request.Path = ServiceDefaultsTestApp.CorrelationPath;
            c.Request.Headers[CorrelationIdFeature.HeaderName] = correlationId;
        });

        activities.Activities.ShouldContain(a => (a.GetTagItem(CorrelationIdFeature.ActivityTagName) as string) == correlationId);
    }

    [Fact]
    public async Task Invoke_ErrorResponse_StillCarriesCorrelationHeader()
    {
        // OnStarting garante o header mesmo em respostas de erro geradas depois (ex.: 400 do middleware de query).
        var context = await _app.SendRawAsync(c =>
        {
            c.Request.Path = ServiceDefaultsTestApp.OkPath;
            c.Request.QueryString = new QueryString("?access_token=x");
            c.Request.Headers[CorrelationIdFeature.HeaderName] = "err-corr-1";
        });

        context.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        context.Response.Headers[CorrelationIdFeature.HeaderName].ToString().ShouldBe("err-corr-1");
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }
}
