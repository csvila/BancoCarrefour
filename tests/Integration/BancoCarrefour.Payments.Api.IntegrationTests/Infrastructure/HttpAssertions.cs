using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BancoCarrefour.Payments.Api.IntegrationTests.Infrastructure;

internal static class HttpAssertions
{
    public const string PingPath = "/v1/ping";

    public static HttpRequestMessage Get(string path, string? bearer = null, string? correlationId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        }

        return request;
    }

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request)
    {
        using (request)
        {
            return await client.SendAsync(request, TestContext.Current.CancellationToken);
        }
    }

    public static async Task<HttpStatusCode> PingStatusAsync(HttpClient client, string? bearer)
    {
        using var response = await SendAsync(client, Get(PingPath, bearer));
        return response.StatusCode;
    }

    /// <summary>Valida o contrato RFC 9457 e devolve o documento para asserções adicionais.</summary>
    public static async Task<JsonElement> ShouldBeProblemDetailsAsync(HttpResponseMessage response, int expectedStatus)
    {
        ArgumentNullException.ThrowIfNull(response);
        ((int)response.StatusCode).ShouldBe(expectedStatus);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement.Clone();

        root.GetProperty("status").GetInt32().ShouldBe(expectedStatus);
        root.TryGetProperty("title", out _).ShouldBeTrue("Problem Details sem 'title'.");
        root.TryGetProperty("type", out _).ShouldBeTrue("Problem Details sem 'type'.");
        root.TryGetProperty("traceId", out _).ShouldBeTrue("Problem Details sem 'traceId'.");
        return root;
    }

    public static string? CorrelationHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Correlation-Id", out var values) ? values.Single() : null;
}
