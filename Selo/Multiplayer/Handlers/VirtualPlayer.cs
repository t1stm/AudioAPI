using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Selo.Multiplayer.Handlers;

public class VirtualPlayer(MessageQueue messageQueue)
{
    /// <summary>Who reported the current item as played out.</summary>
    protected readonly HashSet<string> Finished = [];

    /// <summary>Who reported the current item as buffered.</summary>
    protected readonly HashSet<string> Loaded = [];

    /// <summary>
    ///     Guards every read and write of the room clock, the queue and the two barrier counters, and is held
    ///     across the broadcast so state changes and the frames announcing them leave in the same order. Each
    ///     socket runs its own read loop, so without this the clock is mutated by as many threads as there are
    ///     listeners: <c>StartTime</c> could go null between a <c>HasValue</c> check and the <c>.Value</c> that
    ///     followed it, and <c>Loaded</c> could lose a vote and strand the loading barrier.
    /// </summary>
    /// <remarks>
    ///     ponytail: one lock for the whole room. Public methods take it, <c>*Core</c> helpers assume it is
    ///     already held — SemaphoreSlim is not reentrant, so calling a public one from inside another deadlocks.
    ///     A room is a handful of listeners and MessageQueue already serialises the sends underneath, so this
    ///     costs nothing that was not already serial. Split it per-field if rooms grow past a few dozen members.
    /// </remarks>
    protected readonly SemaphoreSlim Sync = new(1);

    protected int CurrentIndex;

    /// <summary>
    ///     Whether the room is still waiting on everyone to buffer the current track. Only an armed
    ///     barrier may release the clock: a client answers the <c>current</c> frame it gets on the way
    ///     in, and the room does not arm a barrier for a join. That vote used to stand, and the next
    ///     departure made the tally add up — rewinding a mid-track room to zero for everyone left.
    /// </summary>
    protected bool Loading = true;

    protected TimeSpan? PauseTime;
    protected bool Playing = true;

    protected long? StartTime;
    public List<TrackDto> Items { get; set; } = [];

    public async Task Next()
    {
        await Sync.WaitAsync();

        try
        {
            await NextCore();
        }
        finally
        {
            Sync.Release();
        }
    }

    public async Task Previous()
    {
        await Sync.WaitAsync();

        try
        {
            if (CurrentIndex > 0)
                CurrentIndex--;

            UpdateStart();
            await SetPlayingCore(false);
            await Broadcast($"current {CurrentIndex}");
        }
        finally
        {
            Sync.Release();
        }
    }

    public async Task Remove(int index)
    {
        await Sync.WaitAsync();

        try
        {
            if (index < 0 || index >= Items.Count) return;
            var oldCurrent = CurrentIndex;
            Items.RemoveAt(index);

            if (oldCurrent > index)
                CurrentIndex--;

            await Broadcast(QueueMessage());
        }
        finally
        {
            Sync.Release();
        }
    }

    public async Task SetNext(int index)
    {
        await Sync.WaitAsync();

        try
        {
            if (index < 0 || index >= Items.Count || index == CurrentIndex) return;
            if (index < CurrentIndex)
                CurrentIndex--;

            var item = Items[index];
            Items.RemoveAt(index);
            Items.Insert(CurrentIndex + 1, item);

            await Broadcast(QueueMessage());
        }
        finally
        {
            Sync.Release();
        }
    }

    public async Task SkipTo(int index)
    {
        await Sync.WaitAsync();

        try
        {
            if (index < 0 || index >= Items.Count || index == CurrentIndex) return;
            CurrentIndex = index;

            UpdateStart();
            await SetPlayingCore(false);
            await Broadcast($"current {CurrentIndex}");
        }
        finally
        {
            Sync.Release();
        }
    }

    public async Task SetFinished(string id)
    {
        await Sync.WaitAsync();

        try
        {
            Finished.Add(id);
            await HandleFinishedCore();
        }
        finally
        {
            Sync.Release();
        }
    }


    public async Task Shuffle()
    {
        await Sync.WaitAsync();

        try
        {
            Random.Shared.Shuffle(CollectionsMarshal.AsSpan(Items));
            await Broadcast(QueueMessage());
        }
        finally
        {
            Sync.Release();
        }
    }

    public async Task SetPlaying(bool state)
    {
        await Sync.WaitAsync();

        try
        {
            await SetPlayingCore(state);
        }
        finally
        {
            Sync.Release();
        }
    }

    public async Task TogglePlaying()
    {
        await Sync.WaitAsync();

        try
        {
            if (!StartTime.HasValue) return;

            Playing = !Playing;

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

            // Both edges carry the position now, and the position goes out before the
            // state change so a client lands on it before it starts moving. Resuming
            // used to broadcast `playing True` alone, leaving every client to rediscover
            // where the room came back at from the next `sync` — which is a whole round
            // trip of being in the wrong place, on the one transition where everybody
            // is listening for it.
            await Broadcast($"seek {Stopwatch.GetElapsedTime(StartTime.Value).TotalSeconds} {Stamp()}");
            await SetPlayingCore(Playing);
        }
        finally
        {
            Sync.Release();
        }
    }

    public async Task Stop()
    {
        await Sync.WaitAsync();

        try
        {
            Playing = false;
            PauseTime = null;
            await messageQueue.Add("stop");
        }
        finally
        {
            Sync.Release();
        }
    }

    public async Task Enqueue(TrackDto result)
    {
        await Sync.WaitAsync();

        try
        {
            // current sits past the end of the queue exactly when nothing is playing,
            // so the item going in is the one that becomes current
            var startsPlayback = CurrentIndex >= Items.Count;
            Items.Add(result);

            await Broadcast(QueueMessage());
            if (!startsPlayback) return;

            // That is a track change like any other and has to go through the loading
            // barrier. Without it the room plays its first track against whatever the
            // clock already read, so a song added to an idle room starts however many
            // seconds into itself.
            UpdateStart();
            await SetPlayingCore(false);
            await Broadcast($"current {CurrentIndex}");
        }
        finally
        {
            Sync.Release();
        }
    }

    public async Task Joined(User user)
    {
        await Sync.WaitAsync();

        try
        {
            await user.SendAsync(QueueMessage());
            await user.SendAsync($"current {CurrentIndex}");
            await user.SendAsync($"playing {Playing}");

            if (Items.Count > 0)
                await user.SendAsync($"seek {CurrentTimeCore()} {Stamp()}");

            await Broadcast($"chat System %% User '{user.ChatUsername}' joined the session.");
        }
        finally
        {
            Sync.Release();
        }
    }

    public async Task SeekTo(double seconds)
    {
        await Sync.WaitAsync();

        try
        {
            StartTime = Stopwatch.GetTimestamp() - TimeSpanToTimestamp(TimeSpan.FromSeconds(seconds));
            await Broadcast($"seek {Stopwatch.GetElapsedTime(StartTime.Value).TotalSeconds} {Stamp()}");
        }
        finally
        {
            Sync.Release();
        }
    }

    public async Task SetLoaded(string id)
    {
        await Sync.WaitAsync();

        try
        {
            Loaded.Add(id);
            await HandleLoadedCore();
        }
        finally
        {
            Sync.Release();
        }
    }

    /// <summary>
    ///     A member is gone. Both barriers count against the live member list, so a departure moves
    ///     the target and has to be re-checked or the room never advances past this track again. Their
    ///     own votes go with them: a tally that keeps counting someone who has left is a barrier that
    ///     releases for a room that never all agreed.
    /// </summary>
    public async Task UserLeft(string id)
    {
        await Sync.WaitAsync();

        try
        {
            Loaded.Remove(id);
            Finished.Remove(id);

            await HandleLoadedCore();
            await HandleFinishedCore();
        }
        finally
        {
            Sync.Release();
        }
    }

    /// <summary>Answers one user's <c>sync</c>. The position and the stamp are read in the same critical section.</summary>
    public async Task SyncTo(User user)
    {
        await Sync.WaitAsync();

        try
        {
            // The stamp is read at the same instant as the position, so a client can
            // credit the request's queueing to the uplink instead of splitting it across
            // both halves the way `rtt / 2` does.
            await user.SendAsync($"sync {CurrentTimeCore()} {Stamp()}");
        }
        finally
        {
            Sync.Release();
        }
    }

    /// <summary>The room position in seconds, read under the lock.</summary>
    public async Task<double> GetCurrentTime()
    {
        await Sync.WaitAsync();

        try
        {
            return CurrentTimeCore();
        }
        finally
        {
            Sync.Release();
        }
    }

    protected async Task NextCore()
    {
        if (CurrentIndex < Items.Count)
            CurrentIndex++;

        UpdateStart();
        await SetPlayingCore(false);
        await Broadcast($"current {CurrentIndex}");
    }

    protected Task SetPlayingCore(bool state)
    {
        Playing = state;
        return Broadcast($"playing {Playing}");
    }

    protected async Task HandleFinishedCore()
    {
        var count = messageQueue.CurrentStore.Count;
        // An empty room has nobody to wait for and nobody to tell. Without the
        // count check `0 < 0` reads as "everybody reported", so the last user
        // leaving advances the room a track on their way out.
        if (count == 0 || Finished.Count < count) return;

        Finished.Clear();
        await NextCore();
    }

    protected async Task HandleLoadedCore()
    {
        var count = messageQueue.CurrentStore.Count;
        // as in HandleFinishedCore: on an empty room `0 < 0` is false, and this
        // releases the barrier — rewinding the clock and force-playing a room
        // that the next person to join then walks into mid-track. An empty queue
        // is the same mistake in the other direction: starting the clock with
        // nothing to play leaves it running until something is added.
        if (!Loading || count == 0 || Items.Count == 0 || Loaded.Count < count) return;

        Loading = false;
        Loaded.Clear();
        StartTime = Stopwatch.GetTimestamp();

        await Broadcast($"seek {0d} {Stamp()}");
        await SetPlayingCore(true);
    }

    /// <summary>The room position in seconds. Both branches touch <c>StartTime</c>, so the lock must be held.</summary>
    protected double CurrentTimeCore()
    {
        if (!StartTime.HasValue) return 0;

        if (PauseTime.HasValue)
            StartTime = Stopwatch.GetTimestamp() - TimeSpanToTimestamp(PauseTime.Value);

        return Stopwatch.GetElapsedTime(StartTime.Value).TotalSeconds;
    }

    protected void UpdateStart()
    {
        Loading = true;
        Loaded.Clear();
        Finished.Clear();
        StartTime = null;
        PauseTime = null;
    }

    /// <summary>
    ///     The queue frame, serialised straight into a pooled buffer. This used to be the single
    ///     largest allocation in the room: a JSON string, a second string for the interpolation,
    ///     and a third array for the UTF-8 encoding — three copies of the whole queue, all of them
    ///     large enough to reach gen1 on a busy room.
    /// </summary>
    protected Utf8Message QueueMessage()
    {
        var message = new Utf8Message(1024);
        message.Write("queue "u8);

        using var writer = new Utf8JsonWriter(message, CustomSerializer.WriterOptions);
        JsonSerializer.Serialize(writer, Items, CustomSerializer.SerializerOptions);

        return message;
    }

    protected Task Broadcast(Utf8MessageHandler handler)
    {
        return messageQueue.Add(handler.Message);
    }

    protected Task Broadcast(Utf8Message message)
    {
        return messageQueue.Add(message);
    }

    /// <summary>
    ///     When this frame left the room, in Unix milliseconds. Every frame that moves the
    ///     shared clock carries one, so a client can subtract the flight this frame actually
    ///     took rather than the quickest one the link has managed lately — which is what
    ///     half a round trip amounts to, and which understates any frame that got queued.
    /// </summary>
    public static long Stamp()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static long TimeSpanToTimestamp(TimeSpan timeSpan)
    {
        // Multiplying first overflowed: `Ticks * Frequency` is seconds * 10^16
        // wherever the stopwatch counts nanoseconds, which it does on Linux, and
        // that passes long.MaxValue at 922 seconds. Every position past a quarter
        // of an hour came back negative — a seek into a long mix, a pause taken
        // there, and worst of all `GetCurrentTime`, which rebases `StartTime`
        // through here on every `sync` a paused room is asked for.
        // The double carries it exactly: 10^9 * 3600 is well inside 53 bits.
        return (long)(timeSpan.TotalSeconds * Stopwatch.Frequency);
    }
}