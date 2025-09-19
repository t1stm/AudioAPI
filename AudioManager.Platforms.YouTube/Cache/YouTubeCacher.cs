using System.Text.Encodings.Web;
using System.Text.Json;
using AudioManager.Platforms.Errors;
using Result;

namespace AudioManager.Platforms.YouTube.Cache;

public class YouTubeCacher
{
    protected readonly SemaphoreSlim Sync = new(1, 1);
    protected readonly Dictionary<string, YouTubeResult> Cache = new();
    protected readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private const string CacheFolder = "./cache";
    private const string FileName = "YouTube.json";
    private const string CachePath = $"{CacheFolder}/{FileName}";

    private readonly byte[] start_bytes = "["u8.ToArray();
    private readonly byte[] end_bytes = "]"u8.ToArray();
    private readonly byte[] comma_bytes = ","u8.ToArray();

    protected async Task SaveAsync(IEnumerable<YouTubeResult>? delta = null)
    {
        await Sync.WaitAsync();
        Directory.CreateDirectory(CacheFolder);

        var file_info = new FileInfo(CachePath);
        if (delta is not null && file_info is { Exists: true, Length: > 32 })
        {
            await using var file = File.Open(CachePath, FileMode.Open);
            file.SetLength(file.Length - end_bytes.Length);
            file.Seek(0, SeekOrigin.End);
            await file.WriteAsync(comma_bytes);

            var json_bytes = JsonSerializer.SerializeToUtf8Bytes(delta, JsonSerializerOptions);
            await file.WriteAsync(json_bytes.AsMemory()[start_bytes.Length..]);
            await file.FlushAsync();
        }
        else
        {
            await using var file = File.Open(CachePath, FileMode.Create);
            await JsonSerializer.SerializeAsync(file, Cache.Values, JsonSerializerOptions);
            await file.FlushAsync();
        }
        Sync.Release();
    }

    public async Task InitializeAsync()
    {
        var alternativeLookup = Cache.GetAlternateLookup<ReadOnlySpan<char>>();
        var duplicate = false;
        
        await Sync.WaitAsync();
        if (!File.Exists(CachePath))
            return;

        await using var file = File.Open(CachePath, FileMode.Open);
        var deserialized = await JsonSerializer.DeserializeAsync<YouTubeResult[]>(file, JsonSerializerOptions);
        Cache.Clear();

        if (deserialized is null)
            return;

        foreach (var result in deserialized)
        {
            if (!alternativeLookup.TryAdd(result.GetPureID(), result))
                duplicate = true;
        }

        Sync.Release();

        if (duplicate)
        {
            await SaveAsync();
        }
    }

    public async Task AddToCacheAsync(IEnumerable<YouTubeResult> results)
    {
        var cache = Cache.GetAlternateLookup<ReadOnlySpan<char>>();
        await Sync.WaitAsync();
        
        var youtube_results = results.Where(r => !cache.ContainsKey(r.GetPureID())).ToArray();
        foreach (var result in youtube_results)
        {
            cache.TryAdd(result.GetPureID(), result);
        }
        Sync.Release();

        if (youtube_results.Length > 0)
            await SaveAsync(youtube_results);
    }

    public async Task<Result<YouTubeResult, SearchError>> GetFromCacheAsync(string id)
    {
        await Sync.WaitAsync();
        var alternateLookup = Cache.GetAlternateLookup<ReadOnlySpan<char>>();
        var found = alternateLookup.TryGetValue(id, out var result);
        Sync.Release();

        return found && result is not null ? Result<YouTubeResult, SearchError>.Success(result) :
            Result<YouTubeResult, SearchError>.Error(default);
    }
}