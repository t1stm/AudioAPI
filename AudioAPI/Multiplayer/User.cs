using System.Net.WebSockets;

namespace WebApplication3.Multiplayer;

public class User
{
    public required WebSocket WebSocket { get; init; }
    public required string ID { get; init; }
    public string? Username { get; set; }
    
    public string ChatUsername => Username ??= $"Anonymous {string.Concat(ID.TakeWhile((_, i) => i < 5))}";
}