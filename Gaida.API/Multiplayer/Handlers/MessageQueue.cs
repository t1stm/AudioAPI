using System.Text;

namespace Gaida.API.Multiplayer.Handlers;

public class MessageQueue(UserStore store)
{
    protected readonly Queue<string> Messages = new();
    protected readonly SemaphoreSlim Sync = new(1);

    public UserStore CurrentStore => store;

    public async Task Update()
    {
        await Sync.WaitAsync();

        while (Messages.Count > 0)
        {
            var message = Messages.Dequeue();

            var bytes = Encoding.UTF8.GetBytes(message);
            var bytesMemory = new ReadOnlyMemory<byte>(bytes);

            await Parallel.ForEachAsync(store.GetUsers(),
                async (user, _) => { await user.SendMessageAsync(bytesMemory); });
        }

        Sync.Release();
    }

    public async Task Add(string message)
    {
        await Sync.WaitAsync();

        Messages.Enqueue(message);
        Sync.Release();

        await Update();
    }
}