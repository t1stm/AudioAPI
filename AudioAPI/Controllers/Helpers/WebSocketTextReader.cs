using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using Result;

namespace AudioAPI.Controllers.Helpers;

public class WebSocketTextReader
{
    protected readonly StringBuilder _builder = new();
    
    public async Task<Result<string, WebSocketReadStatus>> ReadWholeMessageAsync(WebSocket web_socket)
    {
        _builder.Clear();
        if (web_socket.State != WebSocketState.Open) 
            return Result<string, WebSocketReadStatus>.Error(WebSocketReadStatus.Closed);
        
        using var buffer = MemoryPool<byte>.Shared.Rent(1024 * 32);
        ValueWebSocketReceiveResult receive_result;

        do
        {
            receive_result = await web_socket.ReceiveAsync(buffer.Memory, CancellationToken.None);
            if (receive_result.MessageType != WebSocketMessageType.Text) continue;
            
            var data_slice = buffer.Memory[..receive_result.Count];
            _builder.Append(Encoding.UTF8.GetString(data_slice.Span));

            if (receive_result.MessageType != WebSocketMessageType.Close) continue;
            return Result<string, WebSocketReadStatus>.Error(WebSocketReadStatus.Closed);
        }
        while (!receive_result.EndOfMessage);
        return Result<string, WebSocketReadStatus>.Success(_builder.ToString());
    }
}

public enum WebSocketReadStatus
{
    None,
    Closed
}