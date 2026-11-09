using System.Net;
using BancoCarrefour.ServiceDefaults.Correlation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace BancoCarrefour.ServiceDefaults.UnitTests.Http;

/// <summary>
/// Padrões de HttpClient do ServiceDefaults: propagação de correlation-id e resiliência sem retry cego em métodos
/// não seguros (CLAUDE.md "Nenhum retry cego de operação financeira sem idempotency key"; ADR-0003).
/// O tempo do pipeline de resiliência é controlado por <see cref="FakeTimeProvider"/> (sem esperas reais).
/// </summary>
public sealed class HttpClientDefaultsTests
{
    private const string ClientName = "downstream-probe";
    private static readonly Uri Downstream = new("https://downstream.test.invalid/v1/resource");

    [Fact]
    public async Task OutgoingRequest_WithIncomingCorrelationId_PropagatesHeader()
    {
        var recorder = new RecordingHandler(HttpStatusCode.OK);
        using var provider = Build(recorder, out _);
        SetIncomingCorrelation(provider, "incoming-corr-1");

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var response = await client.GetAsync(Downstream, TestContext.Current.CancellationToken);

        recorder.Requests.ShouldHaveSingleItem().CorrelationId.ShouldBe("incoming-corr-1");
    }

    [Fact]
    public async Task OutgoingRequest_WithExplicitCorrelationHeader_IsNotOverwritten()
    {
        var recorder = new RecordingHandler(HttpStatusCode.OK);
        using var provider = Build(recorder, out _);
        SetIncomingCorrelation(provider, "incoming-corr-2");

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, Downstream);
        request.Headers.TryAddWithoutValidation(CorrelationIdFeature.HeaderName, "explicit-corr");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        recorder.Requests.ShouldHaveSingleItem().CorrelationId.ShouldBe("explicit-corr");
    }

    [Fact]
    public async Task OutgoingRequest_WithoutHttpContext_SendsNoCorrelationHeader()
    {
        var recorder = new RecordingHandler(HttpStatusCode.OK);
        using var provider = Build(recorder, out _);

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var response = await client.GetAsync(Downstream, TestContext.Current.CancellationToken);

        recorder.Requests.ShouldHaveSingleItem().CorrelationId.ShouldBeNull();
    }

    [Fact]
    public async Task SafeMethod_TransientFailure_IsRetriedAtMostTwice()
    {
        var recorder = new RecordingHandler(HttpStatusCode.ServiceUnavailable);
        using var provider = Build(recorder, out var time);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);

        using var response = await SendAdvancingTimeAsync(client, new HttpRequestMessage(HttpMethod.Get, Downstream), time);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        recorder.Requests.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task UnsafeMethod_TransientFailure_IsNeverRetried(string method)
    {
        var recorder = new RecordingHandler(HttpStatusCode.ServiceUnavailable);
        using var provider = Build(recorder, out var time);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        using var request = new HttpRequestMessage(new HttpMethod(method), Downstream) { Content = new StringContent("{}") };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "synthetic-key-1");

        using var response = await SendAdvancingTimeAsync(client, request, time);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        recorder.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SafeMethod_DownstreamHangs_TotalTimeoutIsEnforced()
    {
        // Timeout explícito: a tentativa nunca responde; o pipeline precisa desistir pelo relógio (fake), não pendurar.
        var recorder = new RecordingHandler(HttpStatusCode.OK, hang: true);
        using var provider = Build(recorder, out var time);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);

        var exception = await Should.ThrowAsync<Exception>(async () =>
        {
            using var response = await SendAdvancingTimeAsync(client, new HttpRequestMessage(HttpMethod.Get, Downstream), time);
        });

        exception.ShouldBeAssignableTo<Polly.Timeout.TimeoutRejectedException>();
        recorder.Requests.Count.ShouldBeInRange(1, 3);
    }

    private static async Task<HttpResponseMessage> SendAdvancingTimeAsync(HttpClient client, HttpRequestMessage request, FakeTimeProvider time)
    {
        using (request)
        {
            var send = client.SendAsync(request, TestContext.Current.CancellationToken);
            for (var i = 0; i < 400 && !send.IsCompleted; i++)
            {
                time.Advance(TimeSpan.FromMilliseconds(250));
                await Task.Delay(TimeSpan.FromMilliseconds(5), TestContext.Current.CancellationToken);
            }

            send.IsCompleted.ShouldBeTrue("O pipeline de resiliência não concluiu dentro do tempo simulado.");
            return await send;
        }
    }

    private static void SetIncomingCorrelation(IServiceProvider provider, string correlationId)
    {
        var context = new DefaultHttpContext();
        context.Features.Set<ICorrelationIdFeature>(new CorrelationIdFeature(correlationId));
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
    }

    private static ServiceProvider Build(RecordingHandler recorder, out FakeTimeProvider time)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production", DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = "https://idp.test.invalid/",
            ["Authentication:Audience"] = "api://unit-test",
        });
        time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.AddServiceDefaults();
        builder.Services.AddHttpClient(ClientName).ConfigurePrimaryHttpMessageHandler(() => recorder);
        return builder.Services.BuildServiceProvider();
    }

    private sealed record RecordedRequest(HttpMethod Method, string? CorrelationId);

    private sealed class RecordingHandler(HttpStatusCode status, bool hang = false) : HttpMessageHandler
    {
        private readonly List<RecordedRequest> _requests = [];

        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var correlation = request.Headers.TryGetValues(CorrelationIdFeature.HeaderName, out var values) ? values.Single() : null;
            lock (_requests)
            {
                _requests.Add(new RecordedRequest(request.Method, correlation));
            }

            if (hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.ServiceUnavailable)
            {
                // Retry-After determinístico (o retry padrão honra o header), evitando dependência do jitter.
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            }

            return response;
        }
    }
}
