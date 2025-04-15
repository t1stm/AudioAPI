using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using AudioManager.Platforms;

namespace AudioAPI.Multiplayer;

public class VirtualPlayer(MessageQueue MessageQueue)
{
    public List<PlatformResult> Items { get; set; } = [];

    protected readonly AddedUserHandler Loaded = new();
    protected readonly AddedUserHandler Finished = new();
    protected readonly SemaphoreSlim Sync = new(1);
    protected int CurrentIndex;

    protected long? StartTime;
    protected TimeSpan? PauseTime;
    protected bool Playing = true;

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
        var old_current = CurrentIndex;
        Items.RemoveAt(index);

        if (old_current > index)
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

    public async Task SetFinished(User user)
    {
        await Finished.Add(user);
        await HandleFinished();
    }

    public async Task HandleFinished()
    {
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

        await Broadcast($"chat System %% User \'{user.ChatUsername}\' joined the session.");
    }

    public async Task SeekTo(double seconds)
    {
        await Sync.WaitAsync();

        var wanted_time = TimeSpan.FromSeconds(seconds);
        var current_time = Stopwatch.GetTimestamp();
        var delta_time = current_time - TimeSpanToTimestamp(wanted_time);

        StartTime = delta_time;
        Sync.Release();

        var seconds_broadcast = Stopwatch.GetElapsedTime(StartTime.Value).TotalSeconds;
        await Broadcast(Time(seconds_broadcast));
    }

    public async Task SetLoaded(User user)
    {
        await Loaded.Add(user);
        await HandleLoaded();
    }

    public async Task HandleLoaded()
    {
        if (!Loaded.Fulfilled(MessageQueue)) return;
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
        Loaded.Clear();
        StartTime = null;
        PauseTime = null;
    }

    protected string Queue()
    {
        return $"queue {Items.ToJSON()}";
    }

    protected string Current()
    {
        return $"current {CurrentIndex}";
    }

    protected static string Time(double time)
    {
        return $"seek {time}";
    }

    protected async Task Broadcast(string message)
    {
        await MessageQueue.Add(message);
        await MessageQueue.Update();
    }

    private static long TimeSpanToTimestamp(TimeSpan time_span) => TicksToStopwatchTimestamp(time_span.Ticks);
    private static long TicksToStopwatchTimestamp(long ticks) => ticks * Stopwatch.Frequency / 10000000;
}