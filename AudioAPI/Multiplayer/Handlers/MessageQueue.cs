using System.Net.WebSockets;
using System.Text;

namespace WebApplication3.Multiplayer;

public class MessageQueue(UserStore store)
{
    protected readonly SemaphoreSlim Sync = new(1);
    protected readonly Queue<string> Messages = new();
    
    public UserStore CurrentStore => store;
    public async Task Update()
    {
        await Sync.WaitAsync();

        while (Messages.Count > 0)
        {
            var message = Messages.Dequeue();

            var bytes = Encoding.UTF8.GetBytes(message);
            var bytes_memory = new ReadOnlyMemory<byte>(bytes);

            await Parallel.ForEachAsync(store.GetUsers(), async(user, _) =>
            {
                await SendMessage(bytes_memory, user);
            });
        }
        
        Sync.Release();
    }

    private static async Task SendMessage(ReadOnlyMemory<byte> bytes, User user)
    {
        if (user.WebSocket.State != WebSocketState.Open) return;
        await user.WebSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public async Task Add(string message)
    {
        await Sync.WaitAsync();
        Messages.Enqueue(message);
        Sync.Release();
    }
}