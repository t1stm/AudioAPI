using Gaida.Core.Platforms;
using Gaida.Core.Streams;

namespace Gaida.Platforms.YouTube;

public static class YouTubeCacheProvider
{
    public static readonly SemaphoreSlim CacheLock = new(1, 1);
    public static readonly Dictionary<string, StreamSpreader> CurrentCache = new();

    public static async Task UpdateCache(PlatformResult result, StreamSpreader streamSpreader)
    {
        var alternativeLookup = CurrentCache.GetAlternateLookup<ReadOnlySpan<char>>();

        if (result is not YouTubeResult youtubeResult) return;
        var exportDirectory =
            Environment.GetEnvironmentVariable("YOUTUBE_CACHE", EnvironmentVariableTarget.Process);

        if (exportDirectory is null) return;

        Directory.CreateDirectory(exportDirectory);
        var filePath = Path.Combine(exportDirectory, $"{youtubeResult.GetPureID()}.webm");

        if (File.Exists(filePath)) return;

        await CacheLock.WaitAsync();
        if (!alternativeLookup.TryAdd(filePath, streamSpreader))
        {
            CacheLock.Release();
            return;
        }

        CacheLock.Release();

        var newFile = File.Create(filePath);

        var queue = new Queue<(byte[], int, int)>();
        var syncSemaphore = new SemaphoreSlim(1, 1);

        var streamSubscriber = new StreamSubscriber
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
                await newFile.FlushAsync();
                await newFile.DisposeAsync();

                await CacheLock.WaitAsync();
                alternativeLookup.Remove(filePath);
                CacheLock.Release();
            }
        };

        await streamSpreader.SubscribeAsync(streamSubscriber);
        return;

        async Task SyncCall()
        {
            await syncSemaphore.WaitAsync();

            while (queue.TryDequeue(out var entry))
            {
                var (bytes, offset, length) = entry;
                await newFile.WriteAsync(bytes.AsMemory(offset, length));
            }

            syncSemaphore.Release();
        }
    }
}