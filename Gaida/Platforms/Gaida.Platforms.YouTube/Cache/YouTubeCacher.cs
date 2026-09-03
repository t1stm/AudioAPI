using System.Text.Encodings.Web;
using System.Text.Json;
using Serilog;

namespace Gaida.Platforms.YouTube.Cache;

public class YouTubeCacher(ILogger logger)
{
    private const string CacheFolder = "./cache";
    private const string FileName = "YouTube.json";

    private static readonly string CachePath =
        Environment.GetEnvironmentVariable("YOUTUBE_CACHE_DB", EnvironmentVariableTarget.Process) ??
        $"{CacheFolder}/{FileName}";

    protected readonly Dictionary<string, YouTubeResult> Cache = new();

    protected readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    protected readonly SemaphoreSlim Sync = new(1, 1);
    public ILogger Logger { get; } = logger.ForContext<YouTubeCacher>();

    // ponytail: writes a full snapshot to a temp file and renames it into place, so a crash mid-write
    // never leaves a half-written cache. The old in-place truncate+append trick was faster but could
    // corrupt the file if the process died between the truncate and the write (root cause of a real incident).
    protected async Task SaveAsync()
    {
        await Sync.WaitAsync();
        Logger.Debug("Saving YouTube cache to: {CachePath}", CachePath);
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);

        try
        {
            var tempPath = $"{CachePath}.tmp";
            await using (var file = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(file, Cache.Values, JsonSerializerOptions);
            }

            File.Move(tempPath, CachePath, true);
        }
        catch (Exception e)
        {
            Logger.Fatal("Error while saving YouTube cache: '{@Exception}'", e);
        }
        finally
        {
            Sync.Release();
        }

        Logger.Information("Saved YouTube cache successfully to: {CachePath}", CachePath);
    }

    public async Task InitializeAsync()
    {
        var alternativeLookup = Cache.GetAlternateLookup<ReadOnlySpan<char>>();
        var duplicate = false;

        await Sync.WaitAsync();
        Logger.Information("Loading YouTube cache from: {CachePath}", CachePath);

        try
        {
            if (!File.Exists(CachePath))
                return;

            await using var file = File.Open(CachePath, FileMode.Open);
            var deserialized = await JsonSerializer.DeserializeAsync<YouTubeResult[]>(file, JsonSerializerOptions);
            Cache.Clear();

            if (deserialized is null)
                return;

            foreach (var result in deserialized)
                if (!alternativeLookup.TryAdd(result.GetPureID(), result))
                    duplicate = true;
        }
        catch (Exception e)
        {
            Logger.Fatal("Error while loading YouTube cache: '{@Exception}'", e);
        }
        finally
        {
            Sync.Release();
        }

        if (duplicate) await SaveAsync();
    }

    public async Task AddToCacheAsync(IEnumerable<YouTubeResult> results)
    {
        var cache = Cache.GetAlternateLookup<ReadOnlySpan<char>>();
        await Sync.WaitAsync();
        var youTubeResults = results as YouTubeResult[] ?? results.ToArray();
        Logger.Debug("Adding {Count} YouTube results to cache", youTubeResults.Length);

        var youtubeResults = youTubeResults.Where(r => !cache.ContainsKey(r.GetPureID())).ToArray();
        foreach (var result in youtubeResults) cache.TryAdd(result.GetPureID(), result);
        Sync.Release();

        if (youtubeResults.Length > 0)
            await SaveAsync();
    }

    /// <returns>Up to <paramref name="count" /> distinct cached results, fewer when the cache holds fewer.</returns>
    public async Task<YouTubeResult[]> GetRandomAsync(int count)
    {
        if (count < 1) return [];

        await Sync.WaitAsync();
        // ponytail: copies the whole cache per call; fine for a few thousand entries, reservoir sample if it grows.
        var results = Cache.Values.ToArray();
        Sync.Release();

        Random.Shared.Shuffle(results);
        return results.Length <= count ? results : results[..count];
    }

    /// <returns>The cached result, or <c>null</c> when the ID isn't cached.</returns>
    public async Task<YouTubeResult?> GetFromCacheAsync(string id)
    {
        await Sync.WaitAsync();
        var alternateLookup = Cache.GetAlternateLookup<ReadOnlySpan<char>>();
        alternateLookup.TryGetValue(id, out var result);
        Sync.Release();

        return result;
    }
}