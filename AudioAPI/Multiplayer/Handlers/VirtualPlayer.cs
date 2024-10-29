using System.Text.Encodings.Web;
using System.Text.Json;
using AudioManager.Platforms;

namespace WebApplication3.Multiplayer;

public class VirtualPlayer(MessageQueue messageQueue)
{
    public List<PlatformResult> Items { get; set; } = [];
    
    protected readonly FinishedUserHandler Finished = new();
    protected readonly SemaphoreSlim Sync = new(1);
    protected int CurrentIndex;
    
    protected DateTime? StartTime;
    protected TimeSpan? PauseTime;
    protected bool Playing = true;

    protected JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    
    public async Task Next()
    {
        if (CurrentIndex == Items.Count) return;
        CurrentIndex++;
        
        await BroadcastCurrent();
    }

    public async Task Previous()
    {
        if (CurrentIndex == 0) return;
        CurrentIndex--;

        await BroadcastCurrent();
    }

    public async Task Remove(int index)
    {
        if (index < 0 || index >= Items.Count) return;
        var old_current = CurrentIndex;
        Items.RemoveAt(index);
        
        if (old_current > index)
            CurrentIndex--;

        await BroadcastQueue();
    }
    
    public async Task SetFinished(User user)
    {
        await Finished.Add(user);
        if (!Finished.Fulfilled(messageQueue)) return;
        
        await Next();
    }
    
    public async Task Shuffle()
    {
        var random = new Random();
        var count = Items.Count;
        
        Items = Items.OrderBy(_ => random.Next(count)).ToList();
        await BroadcastQueue();
    }

    public async Task TogglePlaying()
    {
        Playing = !Playing;
        await BroadcastMessage($"playing {Playing}");
        
        PauseTime = Playing switch
        {
            false => DateTime.Now - StartTime,
            true => null
        };
    }
    
    public async Task Stop()
    {
        Playing = false;
        PauseTime = null;
        await BroadcastMessage("stop");
    }

    public async Task Enqueue(PlatformResult result)
    {
        await Sync.WaitAsync();
        Items.Add(result);
        Sync.Release();

        await BroadcastQueue();
    }

    protected async Task BroadcastQueue()
    {
        var serialized = JsonSerializer.Serialize(Items, SerializerOptions);
        
        await BroadcastMessage($"queue {serialized}");
    }

    protected async Task BroadcastCurrent()
    {
        StartTime = DateTime.Now;
        await BroadcastMessage($"current {CurrentIndex}");
    }

    protected async Task BroadcastMessage(string message)
    {
        await messageQueue.Add(message);
        await messageQueue.Update();
    }
}