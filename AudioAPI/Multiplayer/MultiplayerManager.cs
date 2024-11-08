namespace WebApplication3.Multiplayer;

public class MultiplayerManager
{
    protected readonly SemaphoreSlim Sync = new(1);
    protected readonly Dictionary<Guid, Room> Rooms = new();
    protected long ChangeId;

    public async Task<Guid> CreateNewRoom()
    {
        await Sync.WaitAsync();
        var guid = Guid.NewGuid();
        
        Rooms.Add(guid, new Room(guid)
        {
            OnInfoModified = () => ChangeId++
        });
        ChangeId++;
        
        Sync.Release();
        return guid;
    }
    
    public long GetChangeId() => ChangeId;

    public Room? GetRoom(Guid room_id)
    {
        return Rooms.GetValueOrDefault(room_id);
    }

    public ICollection<Room> GetRooms()
    {
        return Rooms.Values;
    }
}