using System.Timers;
using AudioManager.Platforms;
using AudioManager.Platforms.Errors;
using AudioManager.Platforms.Optional;
using AudioManager.Platforms.YouTube;
using AudioManager.Streams;
using Result;
using Result.Objects;

namespace Audio;

public class AudioManager
{
    protected readonly Dictionary<string, Platform> SearchIDMap = [];
    protected readonly Dictionary<string, StreamSpreader> CachedResults = [];
    protected readonly Dictionary<StreamSpreader, DateTime> ExpireTimestamps = [];
    protected readonly SemaphoreSlim Semaphore = new(1, 1);
    protected readonly TimeSpan ExpireTimeSpan = TimeSpan.FromMinutes(30);
    protected System.Timers.Timer? ExpireTimer;
    
    public List<Platform> Platforms { get; } = [
        new YouTube()
    ];

    public void Initialize()
    {
        Platforms.ForEach(MapPlatformIdentifiers);
        Platforms.ForEach(p => p.Initialize());
        ExpireTimer = new System.Timers.Timer
        {
            Interval = 60 * 1000
        };

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
        var split_id = id.Split("://");
        var identifier = split_id[0] + "://";
        var pure_id = split_id.Length > 1 ? string.Join("://", split_id[1..]) : id;
        
        return SearchIDMap.TryGetValue(identifier, out var platform) ? 
            platform.TryID(pure_id, cancellation_token) :
            Task.FromResult(Result<PlatformResult, SearchError>.Error(SearchError.NotFound));
    }

    public async Task<Result<StreamSpreader, DownloadError>> GetContentDownloader(PlatformResult result,
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
                return Result<StreamSpreader, DownloadError>.Error(DownloadError.GenericError);
        
            stream_spreader = downloader.GetOK();
            CachedResults[result.ID] = stream_spreader;
            ExpireTimestamps[stream_spreader] = DateTime.UtcNow.Add(ExpireTimeSpan);
        
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
        
        foreach (var (_, stream_spreader) in CachedResults)
        {
            if (!stream_spreader.Closed) continue;
            if (ExpireTimestamps.ContainsKey(stream_spreader)) continue;
            
            ExpireTimestamps[stream_spreader] = DateTime.UtcNow.Add(ExpireTimeSpan);
        }

        var cached_dictionary = ExpireTimestamps.ToDictionary();
        var now = DateTime.UtcNow;
        
        foreach (var (spreader, expire) in cached_dictionary)
        {
            if (expire < now) continue;
            ExpireTimestamps.Remove(spreader);
            
            await spreader.DisposeAsync();
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
        foreach (var platform in playlist_platforms)
        {
            
        }
        
        return (from platform in playlist_platforms let split_query = query.Split("://") 
            let protocol = $"{split_query[0]}://" 
            where platform.SearchPlaylistIdentifiers.Contains(protocol) 
            select platform).Any() ? QueryType.Playlist : QueryType.Keywords;
    }
}