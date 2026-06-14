using System.Text.Encodings.Web;
using System.Text.Json;
using Gaida.Core.Platforms.Errors;
using Result;
using Serilog;

namespace Gaida.Platforms.YouTube.Cache;

public class YouTubeCacher(ILogger logger)
{
    public ILogger Logger { get; } = logger.ForContext<YouTubeCacher>();
    
    private const string CacheFolder = "./cache";
    private const string FileName = "YouTube.json";
    private const string CachePath = $"{CacheFolder}/{FileName}";
    protected readonly Dictionary<string, YouTubeResult> Cache = new();
    private readonly byte[] _commaBytes = ","u8.ToArray();
    private readonly byte[] _endBytes = "]"u8.ToArray();

    protected readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private readonly byte[] _startBytes = "["u8.ToArray();
    protected readonly SemaphoreSlim Sync = new(1, 1);

    protected async Task SaveAsync(IEnumerable<YouTubeResult>? delta = null)
    {
        await Sync.WaitAsync();
        Logger.Debug("Saving YouTube cache to: {CachePath}", CachePath);
        Directory.CreateDirectory(CacheFolder);

        try
        {
            var fileInfo = new FileInfo(CachePath);
            if (delta is not null && fileInfo is { Exists: true, Length: > 32 })
            {
                await using var file = File.Open(CachePath, FileMode.Open);
                file.SetLength(file.Length - _endBytes.Length);
                file.Seek(0, SeekOrigin.End);
                await file.WriteAsync(_commaBytes);

                var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(delta, JsonSerializerOptions);
                await file.WriteAsync(jsonBytes.AsMemory()[_startBytes.Length..]);
                await file.FlushAsync();
            }
            else
            {
                await using var file = File.Open(CachePath, FileMode.Create);
                await JsonSerializer.SerializeAsync(file, Cache.Values, JsonSerializerOptions);
                await file.FlushAsync();
            }
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
            await SaveAsync(youtubeResults);
    }

    public async Task<Result<YouTubeResult, SearchError>> GetFromCacheAsync(string id)
    {
        await Sync.WaitAsync();
        var alternateLookup = Cache.GetAlternateLookup<ReadOnlySpan<char>>();
        var found = alternateLookup.TryGetValue(id, out var result);
        Sync.Release();

        return found && result is not null
            ? Result<YouTubeResult, SearchError>.Success(result)
            : Result<YouTubeResult, SearchError>.Error(default);
    }
}