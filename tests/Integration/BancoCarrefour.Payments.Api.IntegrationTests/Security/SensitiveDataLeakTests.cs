using BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure;
using BancoCarrefour.TestKit;
using static BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure.HttpAssertions;

namespace BancoCarrefour.Payments.Api.IntegrationTests.Security;

/// <summary>
/// Tokens e PAN canário nunca em logs, spans (tags/eventos/baggage) nem Problem Details
/// (SEC-REQ-006/017; TC-019, TC-150 parte F0, TC-151 parte F0). Asserção sobre canários únicos por execução.
/// </summary>
public sealed class SensitiveDataLeakTests
{
    [Fact]
    public async Task Requests_WithBearerTokensAndPanCanary_NeverLeakToLogsSpansOrProblemDetails()
    {
        using var activities = new ActivityCapture();
        await using var factory = new PaymentsApiFactory();
        using var client = factory.CreateClient();
        var validToken = factory.Tokens.Create();
        var invalidToken = factory.Tokens.Create(s => s.Alg = "none");
        var pan = SensitiveDataDetectors.NewPanCanary();
        var bodies = new List<string>();

        async Task Send(HttpRequestMessage request)
        {
            using var response = await SendAsync(client, request);
            bodies.Add(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        await Send(Get(PingPath, validToken));
        await Send(Get(PingPath, invalidToken));
        await Send(Get($"{PingPath}?access_token={validToken}"));
        await Send(Get($"{PingPath}?id_token={invalidToken}", validToken));
        await Send(Get($"/v1/unknown?pan={pan}", validToken));
        await Send(Get($"{PingPath}?card={pan}"));
        var withHeader = Get(PingPath, validToken);
        withHeader.Headers.TryAddWithoutValidation("X-Card-Number", pan);
        await Send(withHeader);

        var secrets = new[]
        {
            validToken, validToken.Split('.')[1], validToken.Split('.')[2],
            invalidToken, invalidToken.Split('.')[1],
            pan,
        };
        var logs = factory.Logs.AllText;
        var spans = activities.AllText;

        factory.Logs.Entries.ShouldNotBeEmpty("LogCapture precisa ter capturado algo para a asserção ter valor.");
        activities.Activities.ShouldContain(a => a.OperationName.Contains("HttpRequestIn", StringComparison.Ordinal) || a.Kind == System.Diagnostics.ActivityKind.Server);
        foreach (var secret in secrets)
        {
            logs.ShouldNotContain(secret);
            spans.ShouldNotContain(secret);
            bodies.ShouldAllBe(b => !b.Contains(secret, StringComparison.Ordinal));
        }

        SensitiveDataDetectors.Jwt().IsMatch(logs).ShouldBeFalse("Padrão de JWT encontrado nos logs.");
        SensitiveDataDetectors.Jwt().IsMatch(spans).ShouldBeFalse("Padrão de JWT encontrado nos spans.");
    }
}
