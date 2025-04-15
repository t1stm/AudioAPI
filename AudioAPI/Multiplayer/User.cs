using System.Net.WebSockets;
using System.Text;

namespace AudioAPI.Multiplayer;

public class User
{
    public required WebSocket WebSocket { get; init; }
    public required string ID { get; init; }
    public string? Username { get; set; }

    public string ChatUsername => Username ??= $"Anonymous {GetId(ID)}";

    public async Task SendMessageAsync(ReadOnlyMemory<byte> bytes)
    {
        if (WebSocket.State != WebSocketState.Open) return;
        await WebSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static string GetId(string id)
    {
        var index = id.IndexOf(':');
        return index == -1 ? id : id[..index];
    }

    public Task SendMessageAsync(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        return SendMessageAsync(bytes);
    }
}