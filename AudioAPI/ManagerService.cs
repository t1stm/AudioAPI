using System.Diagnostics.CodeAnalysis;
using System.Timers;
using Audio.FFmpeg;
using AudioManager.Platforms.MusicDatabase;
using AudioManager.Platforms.YouTube;

namespace AudioAPI;

public class ManagerService
{
    public readonly Audio.AudioManager AudioManager;
    public readonly System.Timers.Timer ExpireTimer;
    
    protected readonly Dictionary<string, FFmpegEncoder> CachedEncoders = new();
    protected readonly Dictionary<FFmpegEncoder, DateTime> ExpireTimes = new();
    
    private readonly ReaderWriterLockSlim _lock = new();

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
    
    protected int GetKey(ReadOnlySpan<char> codec, int bitrate, ReadOnlySpan<char> id, Span<char> destination)
    {
        const int WANTED_MAX_LENGTH = 128;
        const int MAX_CODEC_LENGTH = 32;
        const int MAX_BITRATE_LENGTH = 11; // this is the max length of an int32 when stringified with a negative sign
        const int MAX_ID_LENGTH = WANTED_MAX_LENGTH - MAX_CODEC_LENGTH - MAX_BITRATE_LENGTH; 
        
        if (codec.Length > MAX_CODEC_LENGTH || id.Length > MAX_ID_LENGTH)
        {
            return -1;
        }
       
        Span<char> bitrateString = stackalloc char[MAX_BITRATE_LENGTH];
        
        codec.CopyTo(destination);
        bitrate.TryFormat(bitrateString, out _);
        bitrateString.CopyTo(destination[codec.Length..]);
        id.CopyTo(destination[(codec.Length + bitrateString.Length)..]);
        
        return codec.Length + bitrateString.Length + id.Length;
    }

    public (string key, FFmpegEncoder encoder) CreateNewEncoder(ReadOnlySpan<char> codec, int bitrate, ReadOnlySpan<char> id)
    {
        Span<char> buffer = stackalloc char[128];
        var keyLength = GetKey(codec, bitrate, id, buffer);
        if (keyLength == -1)
        {
            throw new ArgumentException("Codec or ID is too long");
        }

        ReadOnlySpan<char> key = buffer[..keyLength];
        var alternativeLookup = CachedEncoders.GetAlternateLookup<ReadOnlySpan<char>>();
        
        _lock.EnterReadLock();
        if (alternativeLookup.TryGetValue(key, out var encoder))
            return (key.ToString(), encoder);
        _lock.ExitReadLock();
        
        _lock.EnterWriteLock();
        alternativeLookup.TryAdd(key, encoder = new FFmpegEncoder());
        _lock.ExitWriteLock();
        
        return (key.ToString(), encoder);
    }

    public bool TryGetEncoder(ReadOnlySpan<char> codec, int bitrate, ReadOnlySpan<char> id,
        [NotNullWhen(true)] out FFmpegEncoder? encoder)
    {
        Span<char> buffer = stackalloc char[128];
        var keyLength = GetKey(codec, bitrate, id, buffer);
        if (keyLength == -1)
        {
            encoder = null;
            return false;
        }
        
        ReadOnlySpan<char> key = buffer[..keyLength];
        return TryGetEncoder(key, out encoder);
    }

    public bool TryGetEncoder(ReadOnlySpan<char> key, [NotNullWhen(true)] out FFmpegEncoder? encoder)
    {
        var alternativeLookup = CachedEncoders.GetAlternateLookup<ReadOnlySpan<char>>();

        _lock.EnterReadLock();
        var result = alternativeLookup.TryGetValue(key, out encoder);
        _lock.ExitReadLock();

        return result;
    }

    public void ExpireFFmpegSessions(object? sender, ElapsedEventArgs elapsedEventArgs)
    {
        _lock.EnterWriteLock();

        var expire_copy = ExpireTimes.ToDictionary();
        var now = DateTime.Now;

        foreach (var (ffmpegEncoder, expire) in expire_copy)
        {
            if (expire > now) continue;
            ExpireTimes.Remove(ffmpegEncoder);
            
            var pair = CachedEncoders.FirstOrDefault(kvp => kvp.Value == ffmpegEncoder);
            CachedEncoders.Remove(pair.Key);
            pair.Value.Cleanup();
        }

        _lock.ExitWriteLock();
    }

    public void AddNewExpireSession(FFmpegEncoder encoder, DateTime expireDate)
    {
        _lock.EnterWriteLock();
        ExpireTimes.Add(encoder, expireDate);
        _lock.ExitWriteLock();
    }
}