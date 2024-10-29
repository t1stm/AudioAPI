using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using WebApplication3.Multiplayer;

namespace AudioAPI.Controllers;

public class Multiplayer(ILogger<Multiplayer> logger) : ControllerBase
{
    private static readonly SemaphoreSlim Semaphore = new(1);
    private static readonly MultiplayerManager Manager = new();

    [HttpPost("/Audio/Multiplayer/CreateRoom")]
    public async Task<IActionResult> CreateRoom()
    {
        var room = await Manager.CreateNewRoom();

        return new JsonResult(new
        {
            Room = room
        });
    }
    
    [HttpGet("/WebSocket/Multiplayer/Join")]
    public async Task Join(string room)
    {
        try
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest || !Guid.TryParse(room, out var guid))
            {
                HttpContext.Response.StatusCode = 400;
                return;
            }

            using var web_socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            logger.LogInformation("WebSocket \'{ID}\' connected, with IP: {IP}", HttpContext.TraceIdentifier,
                HttpContext.Connection.RemoteIpAddress);
            await HandleRoomWebSocket(web_socket, guid, HttpContext.TraceIdentifier);
        }
        catch (Exception e)
        {
            logger.LogError(e, "WebSocket \'{ID}\' encountered error", HttpContext.TraceIdentifier);
            throw;
        }
    }

    private async Task HandleRoomWebSocket(WebSocket web_socket, Guid room_id, string id)
    {
        var buffer = new byte[1024 * 32];
        ValueWebSocketReceiveResult receive_result;
        var cached_string = string.Empty;
        
        do
        {
            receive_result = await web_socket.ReceiveAsync(buffer.AsMemory(), CancellationToken.None);
            if (receive_result.MessageType != WebSocketMessageType.Text) continue;
            
            var data_slice = buffer.AsMemory(0, receive_result.Count);
            cached_string += Encoding.UTF8.GetString(data_slice.Span);
            
            if (!receive_result.EndOfMessage) continue;
            
            if (await HandleUserMessage(id, room_id, web_socket, cached_string) != HandleEvent.None)
                break;
            cached_string = string.Empty;
            
        } while (receive_result.MessageType != WebSocketMessageType.Close);
        
        var room = Manager.GetRoom(room_id);
        await (room?.RemoveUser(id) ?? Task.CompletedTask);
        
        await web_socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
        logger.LogInformation("WebSocket \'{ID}\' disconnected", id);
    }

    private async Task<HandleEvent> HandleUserMessage(string id, Guid room_id, WebSocket web_socket, string message)
    {
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("WebSocket \'{ID}\' received: \'{Message}\'", id, message);
        
        await Semaphore.WaitAsync();
        var room = Manager.GetRoom(room_id);
        Semaphore.Release();
        
        if (room is null) return HandleEvent.RoomClosed;
        
        var user = await room.GetOrAddUser(id, web_socket);
        await room.HandleUserMessage(user, message);

        return HandleEvent.None;
    }
}