using System.Timers;
using Audio.FFmpeg;
using AudioManager.Platforms.MusicDatabase;
using AudioManager.Platforms.YouTube;

namespace WebApplication3;

public static class Globals
{
    public static readonly Audio.AudioManager AudioManager;
    
    public static readonly Dictionary<(string codec, int bitrate, string id), FFmpegEncoder> CachedEncoders = new();
    public static readonly Dictionary<(string codec, int bitrate, string id), DateTime> ExpireTimes = new();
    
    public static readonly System.Timers.Timer ExpireTimer;
    public static readonly SemaphoreSlim CacheSemaphore = new(1);
    
    static Globals()
    {
        AudioManager = new Audio.AudioManager();
        AudioManager.Initialize();
        
        AudioManager.RegisterPlatform<YouTube>();
        AudioManager.RegisterPlatform<MusicDatabase>();
        
        ExpireTimer = new System.Timers.Timer();
        ExpireTimer.Interval = 60 * 1000;
        ExpireTimer.Elapsed += ExpireFFmpegSessions;
    }
    
    public static void ExpireFFmpegSessions(object? sender, ElapsedEventArgs elapsedEventArgs)
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