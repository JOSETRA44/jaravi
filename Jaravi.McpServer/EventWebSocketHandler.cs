using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Jaravi.Core;
using Jaravi.Core.Abstractions;

namespace Jaravi.McpServer;

/// <summary>
/// Streams every JaraviEvent to a connected observer (the Dashboard) as JSON
/// text frames. Each connection gets its own bus subscription with its own
/// drop-oldest buffer — a slow observer never affects the engine or other observers.
/// </summary>
public static class EventWebSocketHandler
{
    public static async Task HandleAsync(WebSocket socket, IEventBus bus, CancellationToken ct)
    {
        using var subscription = bus.Subscribe();

        // Half-duplex is enough: we only push. Watch for the client closing.
        var receiveLoop = WatchForCloseAsync(socket, ct);

        try
        {
            await foreach (var evt in subscription.Reader.ReadAllAsync(ct))
            {
                if (socket.State != WebSocketState.Open) break;

                var json = JsonSerializer.Serialize(evt, JaraviJson.Options);
                await socket.SendAsync(
                    Encoding.UTF8.GetBytes(json),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { /* client vanished */ }
        finally
        {
            if (socket.State == WebSocketState.Open)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                catch (WebSocketException) { }
            }
            await receiveLoop;
        }
    }

    private static async Task WatchForCloseAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }
}
