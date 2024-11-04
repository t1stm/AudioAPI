using System.Net.WebSockets;

namespace WebApplication3.Multiplayer;

public class UserStore
{
    protected readonly Dictionary<string, User> Users = [];
    protected readonly SemaphoreSlim Sync = new(1);

    public ICollection<User> GetUsers() => Users.Values;
    public int Count => Users.Count;
    public async Task<User> GetOrAddUser(string id, WebSocket web_socket, Func<User, Task>? onAdd = default)
    {
        await Sync.WaitAsync();
        if (!Users.TryGetValue(id, out var user))
        {
            Users.Add(id, user = new User { ID = id, WebSocket = web_socket });
            Sync.Release();
            var task = onAdd?.Invoke(user);
            if (task != null)
                await task;

            return user;
        }

        Sync.Release();
        return user;
    }

    public async Task RemoveUser(string id)
    {
        await Sync.WaitAsync();
        Users.Remove(id);
        Sync.Release();
    }

    public async Task<User> GetUser(string id)
    {
        await Sync.WaitAsync();
        var user = Users[id];
        Sync.Release();
        return user;
    }
}