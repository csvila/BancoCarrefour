using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BancoCarrefour.TestKit;

/// <summary>Registro de log capturado: mensagem formatada, template/estado e escopos ativos, já serializados em texto.</summary>
public sealed record CapturedLog(string Category, LogLevel Level, EventId EventId, string Message, string State, string Scopes, string? Exception)
{
    public string AllText => $"{Category}|{Message}|{State}|{Scopes}|{Exception}";
}

/// <summary>Provider de log em memória (LogCapture, §5.9 do plano). Captura mensagem, estado estruturado e escopos.</summary>
public sealed class LogCapture : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyCollection<CapturedLog> Entries => [.. _entries];

    public string AllText => string.Join('\n', _entries.Select(e => e.AllText));

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class CaptureLogger(LogCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            var stateText = new StringBuilder();
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var (key, value) in pairs)
                {
                    stateText.Append(key).Append('=').Append(value).Append(';');
                }
            }

            var scopes = new StringBuilder();
            owner._scopes.ForEachScope(
                static (scope, sb) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> scopePairs)
                    {
                        foreach (var (key, value) in scopePairs)
                        {
                            sb.Append(key).Append('=').Append(value).Append(';');
                        }
                    }
                    else if (scope is IEnumerable<KeyValuePair<string, object>> scopePairsNonNull)
                    {
                        foreach (var (key, value) in scopePairsNonNull)
                        {
                            sb.Append(key).Append('=').Append(value).Append(';');
                        }
                    }
                    else
                    {
                        sb.Append(scope).Append(';');
                    }
                },
                scopes);

            owner._entries.Enqueue(new CapturedLog(
                category, logLevel, eventId, formatter(state, exception), stateText.ToString(), scopes.ToString(), exception?.ToString()));
        }
    }
}

/// <summary>
/// Captura global de <see cref="Activity"/> (SpanCapture) via <see cref="ActivityListener"/>, sem depender de exporters
/// adicionais. É process-wide: asserções devem usar canários únicos por teste.
/// </summary>
public sealed class ActivityCapture : IDisposable
{
    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly ActivityListener _listener;

    public ActivityCapture()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = static _ => true,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _activities.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyCollection<Activity> Activities => [.. _activities];

    /// <summary>Texto de todos os nomes, tags, baggage e eventos (com tags) das atividades capturadas.</summary>
    public string AllText
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var activity in _activities)
            {
                sb.Append(activity.OperationName).Append('|').Append(activity.DisplayName).Append('|');
                foreach (var (key, value) in activity.TagObjects)
                {
                    sb.Append(key).Append('=').Append(value).Append(';');
                }

                foreach (var (key, value) in activity.Baggage)
                {
                    sb.Append(key).Append('=').Append(value).Append(';');
                }

                foreach (var activityEvent in activity.Events)
                {
                    sb.Append(activityEvent.Name).Append(':');
                    foreach (var (key, value) in activityEvent.Tags)
                    {
                        sb.Append(key).Append('=').Append(value).Append(';');
                    }
                }

                sb.Append('\n');
            }

            return sb.ToString();
        }
    }

    public void Dispose() => _listener.Dispose();
}

/// <summary>
/// Listener TCP "atacante" em porta efêmera de loopback (TC-008/TC-009). Conta conexões recebidas; qualquer
/// conexão indica que o serviço seguiu uma URL controlada pelo atacante (jku/x5u/kid).
/// </summary>
public sealed class AttackerListener : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private int _connections;

    public AttackerListener()
    {
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public Uri BaseUri => new($"http://127.0.0.1:{Port}/");

    public int Connections => Volatile.Read(ref _connections);

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        try
        {
            await _acceptLoop;
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
            Interlocked.Increment(ref _connections);
        }
    }
}
