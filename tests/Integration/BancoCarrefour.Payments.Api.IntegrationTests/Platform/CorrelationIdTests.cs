using System.Net;
using BancoCarrefour.Payments.Api.IntegrationTests.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using static BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure.HttpAssertions;

namespace BancoCarrefour.Payments.Api.IntegrationTests.Platform;

/// <summary>Correlation-id gerado, ecoado, validado e presente nos logs (NFR-113; TC-212).</summary>
public sealed class CorrelationIdTests(PaymentsApiClassFixture fixture) : IClassFixture<PaymentsApiClassFixture>
{
    private readonly HttpClient _client = fixture.Client;

    [Fact]
    public async Task Request_WithoutCorrelationId_GeneratesOpaqueIdInHeaderAndProblemBody()
    {
        using var response = await SendAsync(_client, Get(PingPath));

        var header = CorrelationHeader(response);
        header.ShouldNotBeNull();
        header.ShouldMatch("^[0-9a-f]{32}$");
        var problem = await ShouldBeProblemDetailsAsync(response, 401);
        problem.GetProperty("correlationId").GetString().ShouldBe(header);
    }

    [Theory]
    [InlineData("abc-123")]
    [InlineData("A.b_c:d-0")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123")]
    public async Task Request_WithValidCorrelationId_EchoesIt(string correlationId)
    {
        using var response = await SendAsync(_client, Get(PingPath, fixture.Factory.Tokens.Create(), correlationId));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        CorrelationHeader(response).ShouldBe(correlationId);
    }

    [Theory]
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234")]
    [InlineData("has space")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("a\"b")]
    [InlineData("ação")]
    [InlineData("line\r\nInjected: 1")]
    [InlineData("tab\tchar")]
    [InlineData("nul\u0001char")]
    [InlineData("")]
    public async Task Request_WithMalformedCorrelationId_ReplacesItAndNeverReflects(string malformed)
    {
        // Envio pelo TestServer (sem a validação de header do HttpClient) para conseguir CRLF/controle.
        var context = await fixture.Factory.Server.SendAsync(
            c =>
            {
                c.Request.Method = HttpMethods.Get;
                c.Request.Path = PingPath;
                c.Request.Headers["X-Correlation-Id"] = malformed;
            },
            TestContext.Current.CancellationToken);

        string echoed = context.Response.Headers["X-Correlation-Id"]!;
        echoed.ShouldMatch("^[0-9a-f]{32}$");
        echoed.ShouldNotBe(malformed);
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        if (malformed.Length > 0)
        {
            body.ShouldNotContain(malformed);
        }
    }

    [Fact]
    public async Task Request_WithMultipleCorrelationIdValues_ReplacesThem()
    {
        var context = await fixture.Factory.Server.SendAsync(
            c =>
            {
                c.Request.Method = HttpMethods.Get;
                c.Request.Path = PingPath;
                c.Request.Headers["X-Correlation-Id"] = new(["first-id", "second-id"]);
            },
            TestContext.Current.CancellationToken);

        string echoed = context.Response.Headers["X-Correlation-Id"]!;
        echoed.ShouldMatch("^[0-9a-f]{32}$");
    }

    [Fact]
    public async Task Request_CorrelationId_IsPresentInLogScope()
    {
        var correlationId = $"tc212-{Guid.NewGuid():N}";

        using var response = await SendAsync(_client, Get($"{PingPath}?access_token=synthetic", correlationId: correlationId));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        fixture.Factory.Logs.Entries.ShouldContain(
            e => e.EventId.Id == 4001 && e.Scopes.Contains($"CorrelationId={correlationId}", StringComparison.Ordinal));
    }
}
