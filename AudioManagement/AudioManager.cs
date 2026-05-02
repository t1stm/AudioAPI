using System.Timers;
using AudioManagement.Platforms;
using AudioManagement.Platforms.Errors;
using AudioManagement.Platforms.Optional.Supports;
using AudioManagement.Streams;
using Result;
using Result.Objects;
using Timer = System.Timers.Timer;

namespace AudioManagement;

public class AudioManager
{
    protected readonly Dictionary<string, StreamSpreader> CachedResults = [];

    protected readonly Timer ExpireTimer = new()
    {
        Interval = 60 * 1000
    };

    protected readonly TimeSpan ExpireTimeSpan = TimeSpan.FromMinutes(45);
    protected readonly Dictionary<string, DateTime> ExpireTimestamps = [];
    protected readonly Dictionary<string, Platform> SearchIDMap = [];

    protected readonly SemaphoreSlim Semaphore = new(1, 1);

    public List<Platform> Platforms { get; } = [];

    protected Dictionary<string, Platform>.AlternateLookup<ReadOnlySpan<char>> SearchIDLookup =>
        SearchIDMap.GetAlternateLookup<ReadOnlySpan<char>>();

    protected Dictionary<string, StreamSpreader>.AlternateLookup<ReadOnlySpan<char>> CachedResultLookup =>
        CachedResults.GetAlternateLookup<ReadOnlySpan<char>>();

    protected Dictionary<string, DateTime>.AlternateLookup<ReadOnlySpan<char>> ExpireTimestampLookup =>
        ExpireTimestamps.GetAlternateLookup<ReadOnlySpan<char>>();

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
        foreach (var identifier in platform.SearchIDIdentifiersLookup.Set) SearchIDLookup.TryAdd(identifier, platform);
    }

    public Task<Result<PlatformResult, SearchError>> SearchID(string id, CancellationToken cancellationToken = default)
    {
        var idSpan = id.AsSpan().Trim();
        Span<Range> platformSplit = stackalloc Range[2];
        var splitCount = idSpan.Trim().Split(platformSplit, "://");

        var splitID = idSpan[platformSplit[0]];
        var identifier = idSpan[..(splitID.Length + 3)];

        return SearchIDLookup.TryGetValue(identifier, out var platform)
            ? platform.TryID(splitCount > 1 ? idSpan[platformSplit[1]].ToString() : id, cancellationToken)
            : Task.FromResult(Result<PlatformResult, SearchError>.Error(SearchError.NotFound));
    }

    public async IAsyncEnumerable<PlatformResult> SearchKeywords(string query)
    {
        var searchTasks = Platforms.OfType<ISupportsSearch>()
            .Select(platform => platform.TrySearchKeywords(query));

        foreach (var task in searchTasks)
        {
            var search = await task;
            if (search == Status.Error) continue;

            foreach (var result in search.GetOk()) yield return result;
        }
    }

    public async IAsyncEnumerable<PlatformResult> SearchPlaylist(string query)
    {
        var searchTasks = Platforms
            .Where(p => p is ISupportsPlaylist pl && pl.IsPlaylistUrl(query))
            .Cast<ISupportsPlaylist>()
            .Select(platform => platform.TrySearchPlaylist(query));

        foreach (var task in searchTasks)
        {
            var search = await task;
            if (search == Status.Error) continue;
            foreach (var result in search.GetOk()) yield return result;
        }
    }

    public async Task<Result<StreamSpreader, DownloadError>> TryGetContentData(PlatformResult result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cachedResults = CachedResultLookup;

            await Semaphore.WaitAsync(cancellationToken);
            if (cachedResults.TryGetValue(result.ID, out var streamSpreader))
                return Result<StreamSpreader, DownloadError>.Success(streamSpreader);

            var downloader = await result.TryGetContentData(cancellationToken);
            if (downloader == Status.Error)
                return Result<StreamSpreader, DownloadError>.Error(DownloadError.Generic);

            streamSpreader = downloader.GetOk();
            CachedResults.Add(result.ID, streamSpreader);
            ExpireTimestamps.Add(result.ID, DateTime.UtcNow.Add(ExpireTimeSpan));

            if (result is ISupportsCaching caching) await caching.RunCacheProcess(streamSpreader);

            return Result<StreamSpreader, DownloadError>.Success(streamSpreader);
        }
        finally
        {
            Semaphore.Release();
        }
    }

    protected async void HandleStreamSpreaders(object? sender, ElapsedEventArgs elapsedEventArgs)
    {
        var expireTimestamps = ExpireTimestampLookup;
        await Semaphore.WaitAsync();
        try
        {
            
            foreach (var (id, streamSpreader) in CachedResults)
            {
                if (!streamSpreader.Closed) continue;
                if (expireTimestamps.ContainsKey(id)) continue;

                expireTimestamps[id] = DateTime.UtcNow.Add(ExpireTimeSpan);
            }

            var cachedDictionary = ExpireTimestamps.ToDictionary();
            var now = DateTime.UtcNow;

            var cachedResults = CachedResultLookup;
            foreach (var (id, expire) in cachedDictionary)
            {
                if (expire < now) continue;
                expireTimestamps.Remove(id);

                var spreader = cachedResults[id];
                await spreader.DisposeAsync();
                cachedResults.Remove(id);
            }

            Semaphore.Release();
        }
        catch (Exception)
        {
            // ignored
            // TODO log with logger
        }
        finally
        {
            Semaphore.Release();
        }
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

        foreach (var (platformID, _) in SearchIDLookup.Dictionary)
        {
            var protocol = GetProtocolSpan(protocolBuffer, platformID);
            if (!querySpan.StartsWith(protocol)) continue;
            return QueryType.ID;
        }

        var playlistPlatforms = Platforms.Where(p => p is ISupportsPlaylist).ToList();
        if (playlistPlatforms
            .Cast<ISupportsPlaylist>()
            .Any(platform => platform.IsPlaylistUrl(query)))
            return QueryType.Playlist;

        Span<Range> platformSplit = stackalloc Range[2];
        foreach (var platform in playlistPlatforms)
        {
            querySpan.Split(platformSplit, "://");
            var protocol = GetProtocolSpan(protocolBuffer, querySpan[platformSplit[0]]);
            if (!platform.SearchPlaylistIdentifiersLookup.Contains(protocol)) continue;
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