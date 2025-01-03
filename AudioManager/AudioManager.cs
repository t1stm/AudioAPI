using System.Timers;
using AudioManager.Platforms;
using AudioManager.Platforms.Errors;
using AudioManager.Platforms.Local;
using AudioManager.Platforms.Optional.Supports;
using AudioManager.Platforms.YouTube;
using AudioManager.Streams;
using Result;
using Result.Objects;

namespace Audio;

public class AudioManager
{
    protected readonly Dictionary<string, Platform> SearchIDMap = [];
    protected readonly Dictionary<string, StreamSpreader> CachedResults = [];
    protected readonly Dictionary<string, DateTime> ExpireTimestamps = [];
    
    protected readonly SemaphoreSlim Semaphore = new(1, 1);
    protected readonly TimeSpan ExpireTimeSpan = TimeSpan.FromMinutes(45);
    protected readonly System.Timers.Timer ExpireTimer = new()
    {
        Interval = 60 * 1000
    };
    
    public List<Platform> Platforms { get; } = [
        new MusicDatabase(),
        new YouTube()
    ];

    public void Initialize()
    {
        Platforms.ForEach(MapPlatformIdentifiers);
        Platforms.ForEach(p => p.Initialize());
        
        ExpireTimer.Elapsed += HandleStreamSpreaders;
        ExpireTimer.Start();
    }

    public void RegisterPlatform(Platform platform)
    {
        platform.Initialize();
        Platforms.Add(platform);
        MapPlatformIdentifiers(platform);
    }

    protected void MapPlatformIdentifiers(Platform platform)
    {
        foreach (var identifier in platform.SearchIDIdentifiers)
        {
            SearchIDMap.Add(identifier, platform);
        }
    }

    public Task<Result<PlatformResult, SearchError>> SearchID(string id, CancellationToken cancellation_token = default)
    {
        var split_id = id.Trim().Split("://");
        var identifier = split_id[0] + "://";
        var pure_id = split_id.Length > 1 ? string.Join("://", split_id[1..]) : id;
        
        return SearchIDMap.TryGetValue(identifier, out var platform) ? 
            platform.TryID(pure_id, cancellation_token) :
            Task.FromResult(Result<PlatformResult, SearchError>.Error(SearchError.NotFound));
    }
    
    public async Task<Result<IEnumerable<PlatformResult>, SearchError>> SearchKeywords(string query)
    {
        var results = new List<PlatformResult>();
        var search_tasks = Platforms
            .Where(p => p is ISupportsSearch)
            .Cast<ISupportsSearch>()
            .Select(platform => platform.TrySearchKeywords(query));
        
        foreach (var task in search_tasks)
        {
            var search = await task;
            if (search == Status.Error) continue;

            results.AddRange(search.GetOK());
        }
        
        return results.Count == 0 ? 
            Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.NotFound) : 
            Result<IEnumerable<PlatformResult>, SearchError>.Success(results);
    }
    
    public async Task<Result<IEnumerable<PlatformResult>, SearchError>> SearchPlaylist(string query)
    {
        var results = new List<PlatformResult>();
        var search_tasks = Platforms
            .Where(p => p is ISupportsPlaylist pl && pl.IsPlaylistUrl(query))
            .Cast<ISupportsPlaylist>()
            .Select(platform => platform.TrySearchPlaylist(query));
        
        foreach (var task in search_tasks)
        {
            var search = await task;
            if (search == Status.Error) continue;

            results.AddRange(search.GetOK());
        }
        
        return results.Count == 0 ? 
            Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.NotFound) : 
            Result<IEnumerable<PlatformResult>, SearchError>.Success(results);
    }

    public async Task<Result<StreamSpreader, DownloadError>> TryGetContentData(PlatformResult result,
        CancellationToken cancellation_token = default)
    {
        try
        {
            await Semaphore.WaitAsync(cancellation_token);
            if (CachedResults.TryGetValue(result.ID, out var stream_spreader))
            {
                return Result<StreamSpreader, DownloadError>.Success(stream_spreader);
            }

            var downloader = await result.TryGetContentData(cancellation_token);
            if (downloader == Status.Error) 
                return Result<StreamSpreader, DownloadError>.Error(DownloadError.Generic);
        
            stream_spreader = downloader.GetOK();
            CachedResults.Add(result.ID, stream_spreader);
            ExpireTimestamps.Add(result.ID, DateTime.UtcNow.Add(ExpireTimeSpan));

            if (result is YouTubeResult youtube_result)
            {
                await YouTubeCacheProvider.UpdateCache(youtube_result, stream_spreader);
            }
        
            return Result<StreamSpreader, DownloadError>.Success(stream_spreader);
        }
        finally
        {
            Semaphore.Release();
        }
    }

    protected async void HandleStreamSpreaders(object? sender, ElapsedEventArgs elapsedEventArgs)
    {
        await Semaphore.WaitAsync();
        
        foreach (var (id, stream_spreader) in CachedResults)
        {
            if (!stream_spreader.Closed) continue;
            if (ExpireTimestamps.ContainsKey(id)) continue;
            
            ExpireTimestamps[id] = DateTime.UtcNow.Add(ExpireTimeSpan);
        }

        var cached_dictionary = ExpireTimestamps.ToDictionary();
        var now = DateTime.UtcNow;
        
        foreach (var (id, expire) in cached_dictionary)
        {
            if (expire < now) continue;
            ExpireTimestamps.Remove(id);
            
            var spreader = CachedResults[id];
            await spreader.DisposeAsync();
            CachedResults.Remove(id);
        }
        
        Semaphore.Release();
    }

    public QueryType FindQueryType(string query)
    {
        foreach (var (platform_id, _) in SearchIDMap)
        {
            var protocol = $"{platform_id}://";
            if (!query.StartsWith(protocol)) continue;
            return QueryType.ID;
        }
        
        var playlist_platforms = Platforms.Where(p => p is ISupportsPlaylist).ToList();
        if (playlist_platforms
            .Cast<ISupportsPlaylist>()
            .Any(platform => platform.IsPlaylistUrl(query)))
        {
            return QueryType.Playlist;
        }
        
        return (from platform in playlist_platforms let split_query = query.Split("://") 
            let protocol = $"{split_query[0]}://" 
            where platform.SearchPlaylistIdentifiers.Contains(protocol) 
            select platform).Any() ? QueryType.Playlist : QueryType.Keywords;
    }

    ~AudioManager()
    {
        ExpireTimer.Stop();
        CachedResults.Clear();
        ExpireTimestamps.Clear();
        SearchIDMap.Clear();
    }
}