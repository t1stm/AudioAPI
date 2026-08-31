using System.Runtime.CompilerServices;
using System.Timers;
using Gaida.Core.Platforms;
using Gaida.Core.Platforms.Optional.Supports;
using Gaida.Core.Streams;
using Gaida.Core.Utils;
using Serilog;
using Timer = System.Timers.Timer;

namespace Gaida.Core;

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

    public AudioManager(ILogger logger)
    {
        Logger = logger.ForContext<AudioManager>();

        ExpireTimer.Elapsed += HandleStreamSpreaders;
        ExpireTimer.Start();
    }

    public ILogger Logger { get; }

    public List<Platform> Platforms { get; } = [];

    protected Dictionary<string, Platform>.AlternateLookup<ReadOnlySpan<char>> SearchIDLookup =>
        SearchIDMap.GetAlternateLookup<ReadOnlySpan<char>>();

    protected Dictionary<string, StreamSpreader>.AlternateLookup<ReadOnlySpan<char>> CachedResultLookup =>
        CachedResults.GetAlternateLookup<ReadOnlySpan<char>>();

    protected Dictionary<string, DateTime>.AlternateLookup<ReadOnlySpan<char>> ExpireTimestampLookup =>
        ExpireTimestamps.GetAlternateLookup<ReadOnlySpan<char>>();

    public void RegisterPlatform(Platform platform)
    {
        platform.Initialize();
        Platforms.Add(platform);

        foreach (var identifier in platform.SearchIDIdentifiersLookup.Set) SearchIDLookup.TryAdd(identifier, platform);
    }

    public T GetPlatform<T>() where T : Platform
    {
        return Platforms.OfType<T>().First();
    }

    /// <returns>The result, or <c>null</c> when no platform claims the ID or the lookup fails.</returns>
    public Task<PlatformResult?> SearchID(string id, CancellationToken cancellationToken = default)
    {
        Logger.Information("Searching for ID: {ID}", id);
        var idSpan = id.AsSpan().Trim();
        Span<Range> platformSplit = stackalloc Range[2];
        var splitCount = idSpan.Split(platformSplit, "://");

        var splitID = idSpan[platformSplit[0]];
        var identifier = idSpan[..(splitID.Length + 3)];

        if (SearchIDLookup.TryGetValue(identifier, out var platform))
            return platform.GetByIdAsync(splitCount > 1 ? idSpan[platformSplit[1]].ToString() : id, cancellationToken);

        Logger.Warning("No platform found for identifier: {Identifier}", identifier.ToString());
        return Task.FromResult<PlatformResult?>(null);
    }

    public async IAsyncEnumerable<PlatformResult> SearchKeywords(string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Logger.Information("Searching for keywords: {Query}", query);
        var totalResults = 0;

        foreach (var platform in Platforms.OfType<ISupportsSearch>())
            await foreach (var result in platform.SearchKeywords(query, cancellationToken)
                               .Guarded(Logger, platform.GetType().Name, cancellationToken))
            {
                totalResults++;
                yield return result;
            }

        Logger.Debug("Keyword search for {Query} finished with {Count} results", query, totalResults);
    }

    public async IAsyncEnumerable<PlatformResult> SearchPlaylist(string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Logger.Information("Searching for playlist: {Query}", query);
        var totalResults = 0;

        foreach (var platform in Platforms.Where(p => p.IsPlaylistUrl(query)).OfType<ISupportsPlaylist>())
            await foreach (var result in platform.SearchPlaylist(query, cancellationToken)
                               .Guarded(Logger, platform.GetType().Name, cancellationToken))
            {
                totalResults++;
                yield return result;
            }

        Logger.Debug("Playlist search for {Query} finished with {Count} results", query, totalResults);
    }

    /// <returns>The shared content stream, or <c>null</c> when no downloader could provide it.</returns>
    public async Task<StreamSpreader?> GetContentDataAsync(PlatformResult result,
        CancellationToken cancellationToken = default)
    {
        await Semaphore.WaitAsync(cancellationToken);
        try
        {
            var cachedResults = CachedResultLookup;
            if (cachedResults.TryGetValue(result.ID, out var streamSpreader))
            {
                Logger.Debug("Cache hit for content ID: {ID}", result.ID);
                return streamSpreader;
            }

            Logger.Information("Fetching content data for ID: {ID}", result.ID);
            streamSpreader = await result.GetContentDataAsync(cancellationToken);
            if (streamSpreader is null)
            {
                Logger.Error("Failed to fetch content data for ID: {ID}", result.ID);
                return null;
            }

            CachedResults.Add(result.ID, streamSpreader);
            ExpireTimestamps.Add(result.ID, DateTime.UtcNow.Add(ExpireTimeSpan));

            if (result is ISupportsCaching caching) await caching.RunCacheProcess(streamSpreader);

            return streamSpreader;
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error while getting content data for ID: {ID}", result.ID);
            return null;
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
                if (expire > now) continue;
                expireTimestamps.Remove(id);

                var spreader = cachedResults[id];
                Logger.Information("Disposing expired stream spreader: {ID}", id);
                await spreader.DisposeAsync();
                cachedResults.Remove(id);
            }
        }
        catch (Exception e)
        {
            Logger.Fatal(e, "Error while handling stream spreaders");
        }
        finally
        {
            Semaphore.Release();
        }
    }

    public QueryType FindQueryType(string query)
    {
        var querySpan = query.AsSpan();
        Span<Range> platformSplit = stackalloc Range[2];

        if (querySpan.Split(platformSplit, "://") > 1)
        {
            var identifier = querySpan[..(querySpan[platformSplit[0]].Length + 3)];

            if (SearchIDLookup.ContainsKey(identifier)) return QueryType.ID;
            foreach (var platform in Platforms)
                if (platform.SearchPlaylistIdentifiersLookup.Contains(identifier))
                    return QueryType.Playlist;
        }

        foreach (var platform in Platforms)
            if (platform.IsPlaylistUrl(querySpan))
                return QueryType.Playlist;

        return QueryType.Keywords;
    }
}
