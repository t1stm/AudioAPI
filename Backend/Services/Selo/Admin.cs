using Gaida.Admin;
using Selo.Multiplayer;

namespace Selo;

/// <summary>
///     Selo's half of the admin surface: what the rooms are doing, and the two ways to moderate them.
/// </summary>
internal static class Admin
{
    public static void MapSeloAdmin(this WebApplication app)
    {
        var manager = app.Services.GetRequiredService<MultiplayerManager>();

        var admin = app.MapAdmin(async () =>
        {
            var rooms = await Task.WhenAll(manager.GetRooms().Select(room => room.Snapshot()));
            return new { count = rooms.Length, rooms };
        });
        if (admin is null) return;

        admin.MapPost("/kick", async (Guid room, string user) =>
        {
            var target = manager.GetRoom(room);
            if (target is null) return Results.NotFound();

            return await target.Kick(user) ? Results.Ok(new { kicked = user }) : Results.NotFound();
        });

        admin.MapPost("/close-room", async (Guid room) =>
        {
            var target = manager.GetRoom(room);
            if (target is null) return Results.NotFound();

            // Kick first, then drop the room. The other order leaves every member holding a socket to
            // a room the manager has forgotten, which nothing would ever close.
            var members = target.UserIds;
            foreach (var member in members) await target.Kick(member);
            manager.RemoveRoom(room);

            return Results.Ok(new { closed = room, removed = members.Count });
        });
    }
}
