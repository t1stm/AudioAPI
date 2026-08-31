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
    protected readonly ConcurrentDictionary<string, FFmpegEncoder> CachedEncoders = new();
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

    public ILogger Logger { get; }
    public Timer ExpireTimer { get; }

    public static string EncoderKey(string codec, int bitrate, string id)
    {
        return $"{codec}-{bitrate}-{id}";
    }

    public FFmpegEncoder CreateEncoder(string key)
    {
        return CachedEncoders.GetOrAdd(key, _ => new FFmpegEncoder());
    }

    public bool TryGetEncoder(string key, [NotNullWhen(true)] out FFmpegEncoder? encoder)
    {
        return CachedEncoders.TryGetValue(key, out encoder);
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
            if (!CachedEncoders.TryRemove(key, out var encoder)) continue;

            Logger.Information("Disposing expired ffmpeg session: {Key}", key);
            encoder.Cleanup();
        }
    }
}
