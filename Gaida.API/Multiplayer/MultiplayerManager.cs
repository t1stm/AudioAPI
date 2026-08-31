namespace Gaida.API.Multiplayer;

public class MultiplayerManager(ManagerService managerService)
{
    protected readonly Dictionary<Guid, Room> Rooms = new();
    protected readonly SemaphoreSlim Sync = new(1);

    /// <summary>Raised when the room list or any room's info changes.</summary>
    public event Func<Task>? RoomsChanged;

    public async Task<Guid> CreateNewRoom()
    {
        await Sync.WaitAsync();
        var guid = Guid.NewGuid();

        Rooms.Add(guid, new Room(guid, managerService)
        {
            OnInfoModified = () => RoomsChanged?.Invoke()
        });
        Sync.Release();

        RoomsChanged?.Invoke();
        return guid;
    }

    public Room? GetRoom(Guid roomID)
    {
        return Rooms.GetValueOrDefault(roomID);
    }

    public ICollection<Room> GetRooms()
    {
        return Rooms.Values;
    }
}
