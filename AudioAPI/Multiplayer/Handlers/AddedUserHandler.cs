namespace AudioAPI.Multiplayer;

public class AddedUserHandler
{
    protected readonly Queue<User> Users = new();
    protected readonly SemaphoreSlim Sync = new(1);

    public void Clear() => Users.Clear();

    public async Task Add(User user)
    {
        await Sync.WaitAsync();
        Users.Enqueue(user);
        Sync.Release();
    }

    public bool Fulfilled(MessageQueue queue)
    {
        if (Users.Count < queue.CurrentStore.Count) return false;
        Users.Clear();
        return true;
    }
}