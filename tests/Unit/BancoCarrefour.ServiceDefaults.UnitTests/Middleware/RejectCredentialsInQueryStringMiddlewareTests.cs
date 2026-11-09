using System.Net;
using BancoCarrefour.ServiceDefaults.UnitTests.Infrastructure;
using Microsoft.Extensions.Logging;

namespace BancoCarrefour.ServiceDefaults.UnitTests.Middleware;

/// <summary>RejectCredentialsInQueryStringMiddleware (ADR-0006 "400"; SEC-REQ-006).</summary>
public sealed class RejectCredentialsInQueryStringMiddlewareTests : IAsyncLifetime
{
    private const string SecretValue = "synthetic-secret-value-5d1e";

    private ServiceDefaultsTestApp _app = null!;

    public async ValueTask InitializeAsync() => _app = await ServiceDefaultsTestApp.StartAsync();

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Theory]
    [InlineData("access_token")]
    [InlineData("id_token")]
    [InlineData("refresh_token")]
    [InlineData("ACCESS_TOKEN")]
    [InlineData("Refresh_Token")]
    [InlineData("iD_tOkEn")]
    public async Task Invoke_ForbiddenParameterAnyCase_Returns400BeforeEndpoint(string parameter)
    {
        using var response = await _app.Client.GetAsync($"{ServiceDefaultsTestApp.OkPath}?{parameter}={SecretValue}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotContain(SecretValue);
        body.ShouldNotContain("\"ok\"");
    }

    [Fact]
    public async Task Invoke_ForbiddenParameterWithEmptyValue_Returns400()
    {
        using var response = await _app.Client.GetAsync($"{ServiceDefaultsTestApp.OkPath}?access_token=", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Invoke_ForbiddenParameterAmongOthers_Returns400()
    {
        using var response = await _app.Client.GetAsync($"{ServiceDefaultsTestApp.OkPath}?page=1&refresh_token={SecretValue}&sort=asc", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("page=1")]
    [InlineData("token_type=bearer")]
    [InlineData("my_access_token=1")]
    [InlineData("access_tokens=1")]
    public async Task Invoke_UnrelatedParameters_PassThrough(string query)
    {
        using var response = await _app.Client.GetAsync($"{ServiceDefaultsTestApp.OkPath}?{query}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invoke_ForbiddenParameter_LogsParameterNameButNeverValue()
    {
        using var response = await _app.Client.GetAsync($"{ServiceDefaultsTestApp.OkPath}?access_token={SecretValue}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var entry = _app.Logs.Entries.Where(e => e.Category.EndsWith("RejectCredentialsInQueryStringMiddleware", StringComparison.Ordinal)).ShouldHaveSingleItem();
        entry.EventId.Id.ShouldBe(4001);
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain("access_token");
        _app.Logs.AllText.ShouldNotContain(SecretValue);
    }
}
