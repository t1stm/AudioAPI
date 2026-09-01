using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Timers;
using Gaida.Core;
using Gaida.Core.FFmpeg;
using Gaida.Platforms.MusicDatabase;
using Gaida.Platforms.YouTube;
using ILogger = Serilog.ILogger;
using Timer = System.Timers.Timer;

namespace Gaida.API;

public class ManagerService
{
    protected readonly ConcurrentDictionary<string, Lazy<Task<FFmpegEncoder?>>> CachedEncoders = new();
    protected readonly ConcurrentDictionary<string, DateTime> ExpireTimes = new();
    public readonly AudioManager Manager;

    public ManagerService(ILogger logger)
    {
        Logger = logger;
        Manager = new AudioManager(logger);

        Manager.RegisterPlatform(new MusicDatabase(logger));
        Manager.RegisterPlatform(new YouTube(logger));

        ExpireTimer = new Timer(TimeSpan.FromMinutes(1)) { Enabled = true };
        ExpireTimer.Elapsed += ExpireFFmpegSessions;
    }

    /// <summary>How long an encoder outlives the request that last asked for it.</summary>
    public static readonly TimeSpan EncoderLifetime = TimeSpan.FromMinutes(45);

    public ILogger Logger { get; }
    public Timer ExpireTimer { get; }

    public static string EncoderKey(string codec, int bitrate, string id)
    {
        return $"{codec}-{bitrate}-{id}";
    }

    /// <summary>
    ///     Starts the encode for <paramref name="key" /> at most once, however many requests race for it: the losers
    ///     await the winner's task instead of spawning a second ffmpeg into the same stream spreader.
    /// </summary>
    /// <param name="start">Feeds the encoder its source. Returns <c>false</c> when the encode could not be started.</param>
    public Task<FFmpegEncoder?> GetOrStartEncoderAsync(string key, Func<FFmpegEncoder, Task<bool>> start)
    {
        // The factory only builds the Lazy, so a losing racer's copy is discarded before it ever runs ffmpeg.
        var lazy = CachedEncoders.GetOrAdd(key, cacheKey => new Lazy<Task<FFmpegEncoder?>>(async () =>
        {
            var encoder = new FFmpegEncoder();
            if (await start(encoder)) return encoder;

            // A failed start must not stay cached, or every later request inherits the failure.
            CachedEncoders.TryRemove(cacheKey, out _);
            ExpireTimes.TryRemove(cacheKey, out _);
            encoder.Cleanup();
            return null;
        }, LazyThreadSafetyMode.ExecutionAndPublication));

        ExpireIn(key, EncoderLifetime);
        return lazy.Value;
    }

    /// <summary>The encode already running or finished for <paramref name="key" />, refreshing its expiry.</summary>
    public bool TryGetEncoder(string key, [NotNullWhen(true)] out Task<FFmpegEncoder?>? encoder)
    {
        if (!CachedEncoders.TryGetValue(key, out var lazy))
        {
            encoder = null;
            return false;
        }

        ExpireIn(key, EncoderLifetime);
        encoder = lazy.Value;
        return true;
    }

    public void ExpireIn(string key, TimeSpan after)
    {
        ExpireTimes[key] = DateTime.UtcNow.Add(after);
    }

    protected void ExpireFFmpegSessions(object? sender, ElapsedEventArgs elapsedEventArgs)
    {
        var now = DateTime.UtcNow;

        foreach (var (key, expire) in ExpireTimes)
        {
            if (expire > now) continue;

            ExpireTimes.TryRemove(key, out _);
            if (!CachedEncoders.TryRemove(key, out var lazy)) continue;

            Logger.Information("Disposing expired ffmpeg session: {Key}", key);
            if (lazy is { IsValueCreated: true, Value.IsCompletedSuccessfully: true }) lazy.Value.Result?.Cleanup();
        }
    }
}
