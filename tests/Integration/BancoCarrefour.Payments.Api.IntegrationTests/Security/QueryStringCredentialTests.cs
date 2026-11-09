using System.Net;
using BancoCarrefour.Payments.Api.IntegrationTests.Authentication;
using BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure;
using static BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure.HttpAssertions;

namespace BancoCarrefour.Payments.Api.IntegrationTests.Security;

/// <summary>Token proibido na query string (ADR-0006 "400"; SEC-REQ-006; TC-018).</summary>
public sealed class QueryStringCredentialTests(PaymentsApiClassFixture fixture) : IClassFixture<PaymentsApiClassFixture>
{
    private readonly HttpClient _client = fixture.Client;

    [Theory]
    [InlineData("access_token")]
    [InlineData("id_token")]
    [InlineData("refresh_token")]
    [InlineData("ACCESS_TOKEN")]
    [InlineData("Access_Token")]
    [InlineData("Id_Token")]
    public async Task Ping_CredentialInQueryWithoutHeader_Returns400ProblemDetailsWithoutEchoingValue(string parameter)
    {
        var token = fixture.Factory.Tokens.Create();

        using var response = await SendAsync(_client, Get($"{PingPath}?{parameter}={token}"));

        var problem = await ShouldBeProblemDetailsAsync(response, 400);
        problem.GetRawText().ShouldNotContain(token);
        problem.GetRawText().ShouldNotContain(token.Split('.')[1]);
    }

    [Theory]
    [InlineData("access_token")]
    [InlineData("id_token")]
    [InlineData("refresh_token")]
    public async Task Ping_CredentialInQueryEvenWithValidHeader_Returns400(string parameter)
    {
        var token = fixture.Factory.Tokens.Create();

        using var response = await SendAsync(_client, Get($"{PingPath}?{parameter}={token}", token));

        await ShouldBeProblemDetailsAsync(response, 400);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/v1/unknown")]
    public async Task AnyPath_CredentialInQuery_Returns400EvenForAnonymousOrUnknownRoutes(string path)
    {
        using var response = await SendAsync(_client, Get($"{path}?access_token=synthetic-not-a-token"));

        await ShouldBeProblemDetailsAsync(response, 400);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("bearer")]
    [InlineData("jwt")]
    public async Task Ping_TokenInOtherQueryParameterWithoutHeader_IsNeverUsedForAuthentication(string parameter)
    {
        // TC-018 (variantes do plano): ADR-0006 só exige 400 para os parâmetros OAuth; para os demais o mínimo
        // inegociável é que o valor nunca autentique a requisição.
        var token = fixture.Factory.Tokens.Create();

        using var response = await SendAsync(_client, Get($"{PingPath}?{parameter}={token}"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ping_CredentialInQuery_TokenValueNeverLogged()
    {
        var token = fixture.Factory.Tokens.Create();

        using var response = await SendAsync(_client, Get($"{PingPath}?access_token={token}"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var logs = fixture.Factory.Logs.AllText;
        logs.ShouldNotContain(token.Split('.')[1]);
        fixture.Factory.Logs.Entries.ShouldContain(e => e.EventId.Id == 4001 && e.Message.Contains("access_token", StringComparison.Ordinal));
    }
}
