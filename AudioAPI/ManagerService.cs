using System.Timers;
using Audio.FFmpeg;
using AudioManager.Platforms.MusicDatabase;
using AudioManager.Platforms.YouTube;

namespace AudioAPI;

public class ManagerService
{
    public readonly Audio.AudioManager AudioManager;

    public readonly Dictionary<(string codec, int bitrate, string id), FFmpegEncoder> CachedEncoders = new();
    public readonly Dictionary<(string codec, int bitrate, string id), DateTime> ExpireTimes = new();

    public readonly System.Timers.Timer ExpireTimer;
    public readonly SemaphoreSlim CacheSemaphore = new(1);

    public ManagerService()
    {
        AudioManager = new Audio.AudioManager();
        AudioManager.Initialize();

        AudioManager.RegisterPlatform<MusicDatabase>();
        AudioManager.RegisterPlatform<YouTube>();

        ExpireTimer = new System.Timers.Timer();
        ExpireTimer.Interval = 60 * 1000;
        ExpireTimer.Elapsed += ExpireFFmpegSessions;
    }

    public void ExpireFFmpegSessions(object? sender, ElapsedEventArgs elapsedEventArgs)
    {
        CacheSemaphore.Wait();

        var expire_copy = ExpireTimes.ToDictionary();
        var now = DateTime.Now;
        foreach (var (tuple, expire) in expire_copy)
        {
            if (expire > now) continue;
            ExpireTimes.Remove(tuple);

            var encoder = CachedEncoders[tuple];
            CachedEncoders.Remove(tuple);

            encoder.Cleanup();
        }

        CacheSemaphore.Release();
    }
}