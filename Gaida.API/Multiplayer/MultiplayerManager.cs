using System.Collections.Concurrent;

namespace Gaida.API.Multiplayer;

public class MultiplayerManager(ManagerService managerService)
{
    // concurrent so the room-list socket can serialise the collection while another request
    // creates a room, which the dictionary-plus-semaphore pair never actually guarded
    protected readonly ConcurrentDictionary<Guid, Room> Rooms = new();

    /// <summary>Raised when the room list or any room's info changes.</summary>
    public event Func<Task>? RoomsChanged;

    public Task<Guid> CreateNewRoom()
    {
        var guid = Guid.NewGuid();

        Rooms.TryAdd(guid, new Room(guid, managerService)
        {
            OnInfoModified = () => RoomsChanged?.Invoke(),
            OnEmptied = () => RemoveRoom(guid)
        });

        RoomsChanged?.Invoke();
        return Task.FromResult(guid);
    }

    public Room? GetRoom(Guid roomID)
    {
        return Rooms.GetValueOrDefault(roomID);
    }

    public ICollection<Room> GetRooms()
    {
        return Rooms.Values;
    }

    /// <summary>
    ///     Drops a room and tells the room-list sockets. Idempotent: two members dropping at once
    ///     both see an empty store, and only the one that wins <c>TryRemove</c> announces it.
    /// </summary>
    public void RemoveRoom(Guid roomID)
    {
        if (!Rooms.TryRemove(roomID, out _)) return;

        RoomsChanged?.Invoke();
    }
}
