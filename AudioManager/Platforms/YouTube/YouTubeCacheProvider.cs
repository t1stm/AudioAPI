using AudioManager.Streams;

namespace AudioManager.Platforms.YouTube;

public static class YouTubeCacheProvider
{
    public static readonly SemaphoreSlim CacheLock = new(1,1);
    public static readonly Dictionary<string, StreamSpreader> CurrentCache = new();
    
    public static async Task UpdateCache(PlatformResult result, StreamSpreader stream_spreader)
    {
        if (result is not YouTubeResult youtube_result) return;
        var export_directory = 
            Environment.GetEnvironmentVariable("YOUTUBE_CACHE", EnvironmentVariableTarget.Process);

        if (export_directory is null) return;
        
        Directory.CreateDirectory(export_directory);
        var file_path = Path.Combine(export_directory, $"{youtube_result.GetPureID()}.webm");
        
        if (File.Exists(file_path)) return;

        await CacheLock.WaitAsync();
        if (!CurrentCache.TryAdd(file_path, stream_spreader))
        {
            CacheLock.Release();
            return;
        }
        CacheLock.Release();
        
        var new_file = File.Create(file_path);
        
        var queue = new Queue<(byte[], int, int)>();
        var sync_semaphore = new SemaphoreSlim(1, 1);

        var stream_subscriber = new StreamSubscriber
        {
            WriteCall = (bytes, offset, length) =>
            {
                queue.Enqueue((bytes, offset, length));
                return Task.FromResult(StreamStatus.Open);
            },
            SyncCall = SyncCall,
            CloseCall = async () =>
            {
                await SyncCall();
                await new_file.FlushAsync();
                await new_file.DisposeAsync();
                
                await CacheLock.WaitAsync();
                CurrentCache.Remove(file_path);
                CacheLock.Release();
            }
        };
        
        stream_spreader.Subscribe(stream_subscriber);
        return;

        async Task SyncCall()
        {
            await sync_semaphore.WaitAsync();

            while (queue.TryDequeue(out var entry))
            {
                var (bytes, offset, length) = entry;
                await new_file.WriteAsync(bytes.AsMemory(offset, length));
            }

            sync_semaphore.Release();
        }
    }
}