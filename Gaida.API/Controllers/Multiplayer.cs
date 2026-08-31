using System.Net.WebSockets;
using System.Text.Json;
using Gaida.API.Controllers.Helpers;
using Gaida.API.Multiplayer;
using Microsoft.AspNetCore.Mvc;

namespace Gaida.API.Controllers;

public class Multiplayer(ILogger<Multiplayer> logger, MultiplayerManager manager) : ControllerBase
{
    [HttpPost("/Audio/Multiplayer/CreateRoom")]
    public async Task<IActionResult> CreateRoom()
    {
        var roomID = await manager.CreateNewRoom();
        logger.LogInformation("Room created: {Room}", roomID);

        return new JsonResult(manager.GetRoom(roomID));
    }

    [HttpGet("/Audio/Multiplayer/Rooms")]
    public async Task<IActionResult> Rooms()
    {
        try
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest) return new BadRequestResult();

            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            logger.LogDebug("Room update websocket '{ID}' connected, with IP: {IP}", HttpContext.TraceIdentifier,
                HttpContext.Connection.RemoteIpAddress);

            await HandleRoomUpdateWebSocket(webSocket, HttpContext.RequestAborted);
        }
        catch (Exception e)
        {
            logger.LogError(e, "WebSocket '{ID}' encountered error", HttpContext.TraceIdentifier);
            throw;
        }

        // the response is the socket itself; a status result on top of it would throw
        return new EmptyResult();
    }

    [HttpGet("/Audio/Multiplayer/Join")]
    public async Task<IActionResult> Join(string room, string? username)
    {
        try
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest || !Guid.TryParse(room, out var guid)) return BadRequest();

            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            logger.LogDebug("WebSocket '{ID}' connected, with IP: {IP}", HttpContext.TraceIdentifier,
                HttpContext.Connection.RemoteIpAddress);
            await HandleRoomJoinWebSocket(webSocket, guid, username, HttpContext.TraceIdentifier,
                HttpContext.RequestAborted);
        }
        catch (Exception e)
        {
            logger.LogError(e, "WebSocket '{ID}' encountered error", HttpContext.TraceIdentifier);
            throw;
        }

        // the response is the socket itself; a status result on top of it would throw
        return new EmptyResult();
    }

    private async Task HandleRoomUpdateWebSocket(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var user = new User
        {
            ID = "dummy user",
            WebSocket = webSocket
        };

        // Pushed by the manager whenever a room is added or renamed; no polling.
        await SendRooms();
        manager.RoomsChanged += SendRooms;

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // client went away
        }
        finally
        {
            manager.RoomsChanged -= SendRooms;
        }

        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        return;

        Task SendRooms()
        {
            return user.SendMessageAsync(JsonSerializer.Serialize(manager.GetRooms()));
        }
    }

    private async Task HandleRoomJoinWebSocket(WebSocket webSocket, Guid roomID, string? username, string id,
        CancellationToken cancellationToken)
    {
        var reader = new WebSocketTextReader();
        var open = await HandleUserMessage(id, roomID, webSocket, string.Empty, username);

        while (open)
        {
            var message = await reader.ReadWholeMessageAsync(webSocket, cancellationToken);
            if (message is null) break;

            open = await HandleUserMessage(id, roomID, webSocket, message);
        }

        var room = manager.GetRoom(roomID);
        await (room?.RemoveUser(id) ?? Task.CompletedTask);

        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
        logger.LogDebug("WebSocket '{ID}' disconnected", id);
    }

    /// <returns>Whether the room is still open.</returns>
    private async Task<bool> HandleUserMessage(string id, Guid roomID, WebSocket webSocket, string message,
        string? initialUsername = null)
    {
        logger.LogDebug("WebSocket '{ID}' received: '{Message}'", id, message);

        var room = manager.GetRoom(roomID);
        if (room is null) return false;

        var user = await room.GetOrAddUser(id, webSocket, initialUsername);
        await room.HandleUserMessage(user, message);

        return true;
    }
}
