using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using AudioManager.Platforms;

namespace WebApplication3.Multiplayer;

public class VirtualPlayer(MessageQueue MessageQueue)
{
    public List<PlatformResult> Items { get; set; } = [];
    
    protected readonly FinishedUserHandler Finished = new();
    protected readonly SemaphoreSlim Sync = new(1);
    protected int CurrentIndex;
    
    protected long StartTime;
    protected TimeSpan? PauseTime;
    protected bool Playing = true;

    protected readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    
    public async Task Next()
    {
        if (CurrentIndex == Items.Count) return;
        CurrentIndex++;

        UpdateStart();
        await Broadcast(Current());
    }

    public async Task Previous()
    {
        if (CurrentIndex == 0) return;
        CurrentIndex--;

        UpdateStart();
        await Broadcast(Current());
    }

    public async Task Remove(int index)
    {
        if (index < 0 || index >= Items.Count) return;
        var old_current = CurrentIndex;
        Items.RemoveAt(index);
        
        if (old_current > index)
            CurrentIndex--;

        await Broadcast(Queue());
    }
    
    public async Task SetFinished(User user)
    {
        await Finished.Add(user);
        if (!Finished.Fulfilled(MessageQueue)) return;
        
        await Next();
    }
    
    public async Task Shuffle()
    {
        var random = new Random();
        var count = Items.Count;
        
        Items = Items.OrderBy(_ => random.Next(count)).ToList();
        await Broadcast(Queue());
    }

    public async Task TogglePlaying()
    {
        Playing = !Playing;
        await Broadcast($"playing {Playing}");
        
        PauseTime = Playing switch
        {
            false => Stopwatch.GetElapsedTime(StartTime),
            true => null
        };
    }
    
    public async Task Stop()
    {
        Playing = false;
        PauseTime = null;
        await Broadcast("stop");
    }

    public async Task Enqueue(PlatformResult result)
    {
        await Sync.WaitAsync();
        Items.Add(result);
        Sync.Release();

        await Broadcast(Queue());
    }
    
    public async Task Joined(User user)
    {
        await user.SendMessageAsync(Queue());
        await user.SendMessageAsync(Current());
        await SyncTime(user);

        await Broadcast($"chat [System] User \'{user.ChatUsername}\' joined the session.");
    }

    public async Task SyncTime(User user)
    {
        var time = await GetCurrentTime();
        await user.SendMessageAsync($"seek {time}");
    }
    
    public async Task<double> GetCurrentTime()
    {
        await Sync.WaitAsync();
        
        var time = PauseTime ?? Stopwatch.GetElapsedTime(StartTime);
        
        Sync.Release();
        
        return time.TotalSeconds;
    }

    protected void UpdateStart()
    {
        StartTime = Stopwatch.GetTimestamp();
    }

    protected string Queue()
    {
        return $"queue {JsonSerializer.Serialize(Items, SerializerOptions)}";
    }

    protected string Current()
    {
        return $"current {CurrentIndex}";
    }

    protected async Task Broadcast(string message)
    {
        await MessageQueue.Add(message);
        await MessageQueue.Update();
    }
}