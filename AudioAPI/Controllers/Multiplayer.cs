using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AudioAPI.Controllers.Helpers;
using Microsoft.AspNetCore.Mvc;
using Result;
using Result.Objects;
using WebApplication3.Multiplayer;

namespace AudioAPI.Controllers;

public class Multiplayer(ILogger<Multiplayer> logger) : ControllerBase
{
    private static readonly SemaphoreSlim Semaphore = new(1);
    private static readonly MultiplayerManager Manager = new();

    [HttpPost("/Audio/Multiplayer/CreateRoom")]
    public async Task<IActionResult> CreateRoom()
    {
        var room_id = await Manager.CreateNewRoom();
        logger.LogInformation("Room created: {Room}", room_id);
        
        var room = Manager.GetRoom(room_id);
        return new JsonResult(room);
    }

    [HttpGet("/Audio/Multiplayer/Rooms")]
    public async Task<IActionResult> Rooms()
    {
        try
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                return new BadRequestResult();
            }

            using var web_socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            logger.LogDebug("Room update websocket \'{ID}\' connected, with IP: {IP}", HttpContext.TraceIdentifier,
                HttpContext.Connection.RemoteIpAddress);
            
            await HandleRoomUpdateWebSocket(web_socket);
        }
        catch (Exception e)
        {
            logger.LogError(e, "WebSocket \'{ID}\' encountered error", HttpContext.TraceIdentifier);
            throw;
        }

        return Ok();
    }
    
    [HttpGet("/Audio/Multiplayer/Join")]
    public async Task<IActionResult> Join(string room)
    {
        try
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest || !Guid.TryParse(room, out var guid))
            {
                return BadRequest();
            }

            using var web_socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            logger.LogDebug("WebSocket \'{ID}\' connected, with IP: {IP}", HttpContext.TraceIdentifier,
                HttpContext.Connection.RemoteIpAddress);
            await HandleRoomJoinWebSocket(web_socket, guid, HttpContext.TraceIdentifier);
        }
        catch (Exception e)
        {
            logger.LogError(e, "WebSocket \'{ID}\' encountered error", HttpContext.TraceIdentifier);
            throw;
        }
        
        return Ok();
    }

    private static async Task HandleRoomUpdateWebSocket(WebSocket web_socket)
    {
        var change_id = Manager.GetChangeId();
        var user = new User
        {
            ID = "dummy user",
            WebSocket = web_socket
        };

        await SendRooms();
        do
        {
            var new_id = Manager.GetChangeId();
            if (change_id == new_id)
            {
                await Task.Delay(166);
                continue;
            }
            change_id = new_id;

            await SendRooms();
        }
        while (web_socket.State == WebSocketState.Open);

        
        await web_socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        return;

        async Task SendRooms()
        {
            var rooms = Manager.GetRooms();
            var serialized = JsonSerializer.Serialize(rooms);
            
            await user.SendMessageAsync(serialized);
        }
    }

    private async Task HandleRoomJoinWebSocket(WebSocket web_socket, Guid room_id, string id)
    {
        var reader = new WebSocketTextReader();
        await HandleUserMessage(id, room_id, web_socket, string.Empty);
        Result<string, WebSocketReadStatus> response;
        do
        {
            response = await reader.ReadWholeMessageAsync(web_socket);
            if (response == Status.Error) break;
            
            var handle = await HandleUserMessage(id, room_id, web_socket, response.GetOK());
            if (handle != HandleEvent.None) break;
            
        } while (response == Status.OK);
        
        var room = Manager.GetRoom(room_id);
        await (room?.RemoveUser(id) ?? Task.CompletedTask);
        
        await web_socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
        logger.LogDebug("WebSocket \'{ID}\' disconnected", id);
    }

    private async Task<HandleEvent> HandleUserMessage(string id, Guid room_id, WebSocket web_socket, string message)
    {
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