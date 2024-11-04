namespace WebApplication3.Multiplayer;

public class MultiplayerManager
{
    protected readonly SemaphoreSlim Sync = new(1);
    protected readonly Dictionary<Guid, Room> Rooms = new();

    public async Task<Guid> CreateNewRoom()
    {
        await Sync.WaitAsync();
        var guid = Guid.NewGuid();
        Rooms.Add(guid, new Room(guid));
        
        Sync.Release();
        return guid;
    }

    public Room? GetRoom(Guid room_id)
    {
        return Rooms.GetValueOrDefault(room_id);
    }

    public ICollection<Room> GetRooms()
    {
        return Rooms.Values;
    }
}