using System.Net.WebSockets;
using System.Text;

namespace WebApplication3.Multiplayer;

public class User
{
    public required WebSocket WebSocket { get; init; }
    public required string ID { get; init; }
    public string? Username { get; set; }
    
    public string ChatUsername => Username ??= $"Anonymous {string.Concat(ID.TakeWhile((_, i) => i < 5))}";

    public async Task SendMessageAsync(ReadOnlyMemory<byte> bytes)
    {
        if (WebSocket.State != WebSocketState.Open) return;
        await WebSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public Task SendMessageAsync(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        return SendMessageAsync(bytes);
    }
}