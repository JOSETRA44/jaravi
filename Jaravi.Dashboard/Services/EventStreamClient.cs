using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Jaravi.Core;
using Jaravi.Core.Events;

namespace Jaravi.Dashboard.Services;

/// <summary>
/// Resilient WebSocket consumer of /ws/events with exponential-backoff
/// reconnection. Raises events on background threads — subscribers marshal
/// to the UI dispatcher themselves.
/// </summary>
public sealed class EventStreamClient(Uri wsUri) : IAsyncDisposable
{
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _cts = new();
    private Task? _runLoop;

    public event Action<JaraviEvent>? EventReceived;
    public event Action<bool>? ConnectionChanged;
    public event Action<string>? Diagnostic;

    public void Start() => _runLoop = RunAsync(_cts.Token);

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = MinBackoff;

        while (!ct.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            try
            {
                await socket.ConnectAsync(wsUri, ct);
                ConnectionChanged?.Invoke(true);
                backoff = MinBackoff;
                await ReceiveLoopAsync(socket, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Diagnostic?.Invoke($"WebSocket error: {ex.Message}"); }

            ConnectionChanged?.Invoke(false);
            try { await Task.Delay(backoff, ct); } catch (OperationCanceledException) { return; }
            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, MaxBackoff.TotalSeconds));
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) return;
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            JaraviEvent? evt = null;
            try { evt = JsonSerializer.Deserialize<JaraviEvent>(json, JaraviJson.Options); }
            catch (JsonException ex) { Diagnostic?.Invoke($"JSON error: {ex.Message}"); }

            if (evt is not null)
                EventReceived?.Invoke(evt);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_runLoop is not null)
            try { await _runLoop; } catch { }
        _cts.Dispose();
    }
}
