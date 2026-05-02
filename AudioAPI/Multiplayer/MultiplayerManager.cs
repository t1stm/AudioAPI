namespace AudioAPI.Multiplayer;

public class MultiplayerManager(ManagerService managerService)
{
    protected readonly Dictionary<Guid, Room> Rooms = new();
    protected readonly SemaphoreSlim Sync = new(1);
    protected long ChangeId;

    public async Task<Guid> CreateNewRoom()
    {
        await Sync.WaitAsync();
        var guid = Guid.NewGuid();

        Rooms.Add(guid, new Room(guid, managerService)
        {
            OnInfoModified = () => ChangeId++
        });
        ChangeId++;

        Sync.Release();
        return guid;
    }

    public long GetChangeId()
    {
        return ChangeId;
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