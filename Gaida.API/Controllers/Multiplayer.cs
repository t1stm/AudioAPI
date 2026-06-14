using System.Net.WebSockets;
using System.Text.Json;
using Gaida.API.Controllers.Helpers;
using Gaida.API.Multiplayer;
using Microsoft.AspNetCore.Mvc;
using Result;
using Result.Objects;

namespace Gaida.API.Controllers;

public class Multiplayer(ILogger<Multiplayer> logger, MultiplayerManager manager) : ControllerBase
{
    private static readonly SemaphoreSlim Semaphore = new(1);

    [HttpPost("/Audio/Multiplayer/CreateRoom")]
    public async Task<IActionResult> CreateRoom()
    {
        var roomID = await manager.CreateNewRoom();
        logger.LogInformation("Room created: {Room}", roomID);

        var room = manager.GetRoom(roomID);
        return new JsonResult(room);
    }

    [HttpGet("/Audio/Multiplayer/Rooms")]
    public async Task<IActionResult> Rooms()
    {
        try
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest) return new BadRequestResult();

            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            logger.LogDebug("Room update websocket \'{ID}\' connected, with IP: {IP}", HttpContext.TraceIdentifier,
                HttpContext.Connection.RemoteIpAddress);

            await HandleRoomUpdateWebSocket(webSocket);
        }
        catch (Exception e)
        {
            logger.LogError(e, "WebSocket \'{ID}\' encountered error", HttpContext.TraceIdentifier);
            throw;
        }

        return Ok();
    }

    [HttpGet("/Audio/Multiplayer/Join")]
    public async Task<IActionResult> Join(string room, string? username)
    {
        try
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest || !Guid.TryParse(room, out var guid)) return BadRequest();

            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            logger.LogDebug("WebSocket \'{ID}\' connected, with IP: {IP}", HttpContext.TraceIdentifier,
                HttpContext.Connection.RemoteIpAddress);
            await HandleRoomJoinWebSocket(webSocket, guid, username, HttpContext.TraceIdentifier,
                HttpContext.RequestAborted);
        }
        catch (Exception e)
        {
            logger.LogError(e, "WebSocket \'{ID}\' encountered error", HttpContext.TraceIdentifier);
            throw;
        }

        return Ok();
    }

    private async Task HandleRoomUpdateWebSocket(WebSocket webSocket, CancellationToken? cancellationToken = null)
    {
        cancellationToken ??= CancellationToken.None;
        var changeID = manager.GetChangeId();
        var user = new User
        {
            ID = "dummy user",
            WebSocket = webSocket
        };

        await SendRooms();
        do
        {
            var newID = manager.GetChangeId();
            if (changeID == newID)
            {
                await Task.Delay(166);
                continue;
            }

            changeID = newID;

            await SendRooms();
        } while (webSocket.State == WebSocketState.Open && !cancellationToken.Value.IsCancellationRequested);


        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        return;

        async Task SendRooms()
        {
            var rooms = manager.GetRooms();
            var serialized = JsonSerializer.Serialize(rooms);

            await user.SendMessageAsync(serialized);
        }
    }

    private async Task HandleRoomJoinWebSocket(WebSocket webSocket, Guid roomID, string? username, string id,
        CancellationToken cancellationToken)
    {
        var reader = new WebSocketTextReader(logger);
        await HandleUserMessage(id, roomID, webSocket, string.Empty, username);
        Result<string, WebSocketReadStatus> response;
        do
        {
            response = await reader.ReadWholeMessageAsync(webSocket, cancellationToken);
            if (response == Status.Error) break;

            var handle = await HandleUserMessage(id, roomID, webSocket, response.GetOk());
            if (handle != HandleEvent.None) break;
        } while (response == Status.Ok);

        var room = manager.GetRoom(roomID);
        await (room?.RemoveUser(id) ?? Task.CompletedTask);

        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
        logger.LogDebug("WebSocket \'{ID}\' disconnected", id);
    }

    private async Task<HandleEvent> HandleUserMessage(string id, Guid roomID, WebSocket webSocket, string message,
        string? initialUsername = null)
    {
        logger.LogDebug("WebSocket \'{ID}\' received: \'{Message}\'", id, message);

        await Semaphore.WaitAsync();
        var room = manager.GetRoom(roomID);
        Semaphore.Release();

        if (room is null) return HandleEvent.RoomClosed;

        var user = await room.GetOrAddUser(id, webSocket, initialUsername);
        await room.HandleUserMessage(user, message);

        return HandleEvent.None;
    }
}