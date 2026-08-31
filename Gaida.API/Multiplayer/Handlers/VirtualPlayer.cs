using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Gaida.Core.Platforms;

namespace Gaida.API.Multiplayer.Handlers;

public class VirtualPlayer(MessageQueue messageQueue)
{
    protected readonly SemaphoreSlim Sync = new(1);
    protected int CurrentIndex;

    /// <summary>How many users reported the current item as played out.</summary>
    protected int FinishedCount;

    /// <summary>How many users reported the current item as buffered.</summary>
    protected int LoadedCount;

    protected TimeSpan? PauseTime;
    protected bool Playing = true;

    protected long? StartTime;
    public List<PlatformResult> Items { get; set; } = [];

    public async Task Next()
    {
        if (CurrentIndex < Items.Count)
            CurrentIndex++;

        UpdateStart();
        await SetPlaying(false);
        await Broadcast(Current());
    }

    public async Task Previous()
    {
        if (CurrentIndex > 0)
            CurrentIndex--;

        UpdateStart();
        await SetPlaying(false);
        await Broadcast(Current());
    }

    public async Task Remove(int index)
    {
        if (index < 0 || index >= Items.Count) return;
        var oldCurrent = CurrentIndex;
        Items.RemoveAt(index);

        if (oldCurrent > index)
            CurrentIndex--;

        await Broadcast(Queue());
    }

    public async Task SetNext(int index)
    {
        if (index < 0 || index >= Items.Count || index == CurrentIndex) return;
        if (index < CurrentIndex)
            CurrentIndex--;

        var item = Items[index];
        Items.RemoveAt(index);
        Items.Insert(CurrentIndex + 1, item);

        await Broadcast(Queue());
    }

    public async Task SkipTo(int index)
    {
        if (index < 0 || index >= Items.Count || index == CurrentIndex) return;
        CurrentIndex = index;

        UpdateStart();
        await SetPlaying(false);
        await Broadcast(Current());
    }

    public async Task SetFinished()
    {
        FinishedCount++;
        await HandleFinished();
    }

    public async Task HandleFinished()
    {
        if (FinishedCount < messageQueue.CurrentStore.Count) return;

        FinishedCount = 0;
        await Next();
    }

    public async Task Shuffle()
    {
        Random.Shared.Shuffle(CollectionsMarshal.AsSpan(Items));
        await Broadcast(Queue());
    }

    public async Task SetPlaying(bool state)
    {
        Playing = state;
        await Broadcast($"playing {Playing}");
    }

    public async Task TogglePlaying()
    {
        if (!StartTime.HasValue) return;

        await SetPlaying(!Playing);

        switch (Playing)
        {
            case false:
                PauseTime = Stopwatch.GetElapsedTime(StartTime.Value);
                break;
            case true:
                if (PauseTime.HasValue)
                    StartTime = Stopwatch.GetTimestamp() - TimeSpanToTimestamp(PauseTime.Value);
                PauseTime = null;
                break;
        }

        if (!Playing)
        {
            var seconds = Stopwatch.GetElapsedTime(StartTime.Value).TotalSeconds;
            await Broadcast(Time(seconds));
        }
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
        await user.SendMessageAsync($"playing {Playing}");

        if (Items.Count > 0)
            await SyncTime(user);

        await Broadcast($"chat System %% User '{user.ChatUsername}' joined the session.");
    }

    public async Task SeekTo(double seconds)
    {
        await Sync.WaitAsync();

        var wantedTime = TimeSpan.FromSeconds(seconds);
        var currentTime = Stopwatch.GetTimestamp();
        var deltaTime = currentTime - TimeSpanToTimestamp(wantedTime);

        StartTime = deltaTime;
        Sync.Release();

        var secondsBroadcast = Stopwatch.GetElapsedTime(StartTime.Value).TotalSeconds;
        await Broadcast(Time(secondsBroadcast));
    }

    public async Task SetLoaded()
    {
        LoadedCount++;
        await HandleLoaded();
    }

    public async Task HandleLoaded()
    {
        if (LoadedCount < messageQueue.CurrentStore.Count) return;

        LoadedCount = 0;
        StartTime = Stopwatch.GetTimestamp();

        await Broadcast(Time(0));
        await SetPlaying(true);
    }

    public async Task SyncTime(User user)
    {
        var time = await GetCurrentTime();
        await user.SendMessageAsync($"seek {time}");
    }

    public async Task<double> GetCurrentTime()
    {
        if (!StartTime.HasValue) return 0;

        await Sync.WaitAsync();

        if (PauseTime.HasValue)
            StartTime = Stopwatch.GetTimestamp() - TimeSpanToTimestamp(PauseTime.Value);

        var time = Stopwatch.GetElapsedTime(StartTime.Value);

        Sync.Release();

        return time.TotalSeconds;
    }

    protected void UpdateStart()
    {
        LoadedCount = 0;
        StartTime = null;
        PauseTime = null;
    }

    protected string Queue()
    {
        return $"queue {JsonSerializer.Serialize(Items, CustomSerializer.SerializerOptions)}";
    }

    protected string Current()
    {
        return $"current {CurrentIndex}";
    }

    protected static string Time(double time)
    {
        return $"seek {time}";
    }

    protected Task Broadcast(string message)
    {
        return messageQueue.Add(message);
    }

    private static long TimeSpanToTimestamp(TimeSpan timeSpan)
    {
        return timeSpan.Ticks * Stopwatch.Frequency / 10000000;
    }
}
