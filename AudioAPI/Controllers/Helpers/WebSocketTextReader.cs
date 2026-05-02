using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using Result;

namespace AudioAPI.Controllers.Helpers;

public class WebSocketTextReader(ILogger<Multiplayer> logger)
{
    protected readonly StringBuilder Builder = new();

    public async Task<Result<string, WebSocketReadStatus>> ReadWholeMessageAsync(WebSocket webSocket,
        CancellationToken? cancellationToken = null)
    {
        try
        {
            cancellationToken ??= CancellationToken.None;
            Builder.Clear();
            if (webSocket.State != WebSocketState.Open)
                return Result<string, WebSocketReadStatus>.Error(WebSocketReadStatus.Closed);

            using var buffer = MemoryPool<byte>.Shared.Rent(1024 * 32);
            ValueWebSocketReceiveResult receiveResult;

            do
            {
                receiveResult = await webSocket.ReceiveAsync(buffer.Memory, cancellationToken.Value);
                if (receiveResult.MessageType != WebSocketMessageType.Text) continue;

                var dataSlice = buffer.Memory[..receiveResult.Count];
                Builder.Append(Encoding.UTF8.GetString(dataSlice.Span));

                if (receiveResult.MessageType != WebSocketMessageType.Close) continue;
                return Result<string, WebSocketReadStatus>.Error(WebSocketReadStatus.Closed);
            } while (!receiveResult.EndOfMessage);

            return Result<string, WebSocketReadStatus>.Success(Builder.ToString());
        }
        catch (Exception e)
        {
            return Result<string, WebSocketReadStatus>.Error(WebSocketReadStatus.Unknown);
        }
    }
}

public enum WebSocketReadStatus
{
    None,
    Closed,
    Unknown
}