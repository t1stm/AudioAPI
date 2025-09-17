using System.Timers;
using AudioManager.Platforms;
using AudioManager.Platforms.Errors;
using AudioManager.Platforms.Optional.Supports;
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

    public List<Platform> Platforms { get; } = [];

    public void Initialize()
    {
        Platforms.ForEach(MapPlatformIdentifiers);
        Platforms.ForEach(p => p.Initialize());

        ExpireTimer.Elapsed += HandleStreamSpreaders;
        ExpireTimer.Start();
    }

    public void RegisterPlatform<T>() where T : Platform, new()
    {
        var platform = new T();
        platform.Initialize();

        Platforms.Add(platform);
        MapPlatformIdentifiers(platform);
    }

    public T GetPlatform<T>() where T : Platform
    {
        var platform = Platforms.FirstOrDefault(p => p.GetType() == typeof(T));
        ArgumentNullException.ThrowIfNull(platform);
        return (platform as T)!;
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
        var idSpan = id.AsSpan();
        Span<Range> platformSplit = stackalloc Range[2];
        idSpan.Trim().Split(platformSplit, "://");

        var split_id = idSpan[platformSplit[0]];
        var identifier = split_id[0] + "://";

        return SearchIDMap.TryGetValue(identifier, out var platform)
            ? platform.TryID(split_id.Length > 1 ? split_id[1..].ToString() : id, cancellation_token)
            : Task.FromResult(Result<PlatformResult, SearchError>.Error(SearchError.NotFound));
    }

    public async IAsyncEnumerable<PlatformResult> SearchKeywords(string query)
    {
        var search_tasks = Platforms
            .Where(p => p is ISupportsSearch)
            .Cast<ISupportsSearch>()
            .Select(platform => platform.TrySearchKeywords(query));

        foreach (var task in search_tasks)
        {
            var search = await task;
            if (search == Status.Error) continue;

            foreach (var result in search.GetOK())
            {
                yield return result;
            }
        }
    }

    public async IAsyncEnumerable<PlatformResult> SearchPlaylist(string query)
    {
        var search_tasks = Platforms
            .Where(p => p is ISupportsPlaylist pl && pl.IsPlaylistUrl(query))
            .Cast<ISupportsPlaylist>()
            .Select(platform => platform.TrySearchPlaylist(query));

        foreach (var task in search_tasks)
        {
            var search = await task;
            if (search == Status.Error) continue;
            foreach (var result in search.GetOK())
            {
                yield return result;
            }
        }
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

            if (result is ISupportsCaching caching)
            {
                await caching.RunCacheProcess(stream_spreader);
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

    private static ReadOnlySpan<char> GetProtocolSpan(Span<char> buffer, ReadOnlySpan<char> platformID)
    {
        const string protocolSeparator = "://";
        platformID.CopyTo(buffer);
        protocolSeparator.CopyTo(buffer[platformID.Length..]);
        return buffer[..(platformID.Length + protocolSeparator.Length)];
    }

    public QueryType FindQueryType(string query)
    {
        var querySpan = query.AsSpan();
        Span<char> protocolBuffer = stackalloc char[32];

        foreach (var (platform_id, _) in SearchIDMap)
        {
            var protocol = GetProtocolSpan(protocolBuffer, platform_id);
            if (!querySpan.StartsWith(protocol)) continue;
            return QueryType.ID;
        }

        var playlist_platforms = Platforms.Where(p => p is ISupportsPlaylist).ToList();
        if (playlist_platforms
            .Cast<ISupportsPlaylist>()
            .Any(platform => platform.IsPlaylistUrl(query)))
        {
            return QueryType.Playlist;
        }

        Span<Range> platformSplit = stackalloc Range[2];
        foreach (var platform in playlist_platforms)
        {
            querySpan.Split(platformSplit, "://");
            var protocol = GetProtocolSpan(protocolBuffer, querySpan[platformSplit[0]]);
            if (!platform.SearchPlaylistIdentifiers.GetAlternateLookup<ReadOnlySpan<char>>()
                    .Contains(protocol)) continue;
            return QueryType.Playlist;
        }

        return QueryType.Keywords;
    }

    ~AudioManager()
    {
        ExpireTimer.Stop();
        CachedResults.Clear();
        ExpireTimestamps.Clear();
        SearchIDMap.Clear();
    }
}