namespace WebApplication3.Multiplayer;

public class FinishedUserHandler
{
    protected readonly Queue<User> Users = new();
    protected readonly SemaphoreSlim Sync = new(1);

    public async Task Add(User user)
    {
        await Sync.WaitAsync();
        Users.Enqueue(user);
        Sync.Release();
    }
    
    public bool Fulfilled(MessageQueue queue)
    {
        if (Users.Count != queue.CurrentStore.Count) return false;
        Users.Clear();
        return true;
    }
}