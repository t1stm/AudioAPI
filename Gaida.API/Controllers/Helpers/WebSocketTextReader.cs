using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace Gaida.API.Controllers.Helpers;

public class WebSocketTextReader
{
    protected readonly StringBuilder Builder = new();

    /// <returns>The whole text message, or <c>null</c> when the socket closed or faulted.</returns>
    public async Task<string?> ReadWholeMessageAsync(WebSocket webSocket,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Builder.Clear();
            if (webSocket.State != WebSocketState.Open) return null;

            using var buffer = MemoryPool<byte>.Shared.Rent(1024 * 32);
            ValueWebSocketReceiveResult receiveResult;

            do
            {
                receiveResult = await webSocket.ReceiveAsync(buffer.Memory, cancellationToken);
                if (receiveResult.MessageType == WebSocketMessageType.Close) return null;
                if (receiveResult.MessageType != WebSocketMessageType.Text) continue;

                Builder.Append(Encoding.UTF8.GetString(buffer.Memory[..receiveResult.Count].Span));
            } while (!receiveResult.EndOfMessage);

            return Builder.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
