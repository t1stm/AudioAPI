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

    protected async Task SaveAsync()
    {
        await Sync.WaitAsync();
        Directory.CreateDirectory(CacheFolder);
        
        await using var file = File.Open(CachePath, FileMode.Create);
        await JsonSerializer.SerializeAsync(file, Cache.Values, JsonSerializerOptions);
        Sync.Release();
    }

    public async Task InitializeAsync()
    {
        try
        {
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
                Cache.Add(result.GetPureID(), result);
            }
        }
        finally
        {
            Sync.Release();
        }
    }
    
    public async Task AddToCacheAsync(YouTubeResult result)
    {
        await Sync.WaitAsync();
        Cache.TryAdd(result.GetPureID(), result);
        Sync.Release();
        
        await SaveAsync();
    }

    public async Task AddToCacheAsync(IEnumerable<YouTubeResult> results)
    {
        await Sync.WaitAsync();
        foreach (var result in results)
        {
            Cache.TryAdd(result.GetPureID(), result);
        }
        Sync.Release();
        
        await SaveAsync();
    }

    public async Task<Result<YouTubeResult, SearchError>> GetFromCacheAsync(string id)
    {
        await Sync.WaitAsync();
        var found = Cache.TryGetValue(id, out var result);
        Sync.Release();
        
        return found && result is not null ? Result<YouTubeResult, SearchError>.Success(result) :  
            Result<YouTubeResult, SearchError>.Error(default);
    }
}