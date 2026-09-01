using System.Text;

namespace Gaida.API.Multiplayer.Handlers;

public class MessageQueue(UserStore store)
{
    protected readonly SemaphoreSlim Sync = new(1);

    public UserStore CurrentStore => store;

    /// <summary>Broadcasts an interpolated frame without ever materialising it as a string.</summary>
    public Task Send(Utf8MessageHandler handler)
    {
        return Add(handler.Message);
    }

    public Task Add(string message)
    {
        var pooled = new Utf8Message(Encoding.UTF8.GetMaxByteCount(message.Length));
        pooled.Write(message.AsSpan());

        return Add(pooled);
    }

    /// <summary>Sends one pooled frame to every member, then returns its buffer.</summary>
    public async Task Add(Utf8Message message)
    {
        await Sync.WaitAsync();
        try
        {
            // ponytail: one socket at a time. A room is a handful of listeners and the frames
            // are a few dozen bytes, so Parallel.ForEachAsync was costing more per broadcast in
            // scheduling and closure allocations than it saved in latency — and it waited for
            // every send anyway, so a stalled peer blocked the room either way. Fan back out if
            // rooms ever grow past a few dozen members.
            foreach (var (_, user) in store.Users)
                try
                {
                    await user.SendMessageAsync(message.Memory);
                }
                catch (Exception)
                {
                    // A peer that dropped between the state check and the send must not take the frame
                    // away from everybody after it in the enumeration: one bad connection used to leave
                    // the rest of the room without the `seek` or `playing` that had just gone out, and
                    // they stayed at the old position until something else moved the clock. The socket's
                    // own read loop reaps it from the store on its way out.
                }
        }
        finally
        {
            Sync.Release();
            message.Dispose();
        }
    }
}
