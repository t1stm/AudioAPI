using System.Runtime.CompilerServices;
using Gaida.Core.Platforms;
using Gaida.Core.Platforms.Optional.Supports;
using Gaida.Core.Utils;
using Serilog;

namespace Gaida.Core;

public class AudioManager(ILogger logger)
{
    protected readonly Dictionary<string, Platform> SearchIDMap = [];

    public ILogger Logger { get; } = logger.ForContext<AudioManager>();

    public List<Platform> Platforms { get; } = [];

    protected Dictionary<string, Platform>.AlternateLookup<ReadOnlySpan<char>> SearchIDLookup =>
        SearchIDMap.GetAlternateLookup<ReadOnlySpan<char>>();

    public void RegisterPlatform(Platform platform)
    {
        platform.Initialize();
        Platforms.Add(platform);

        foreach (var identifier in platform.SearchIDIdentifiersLookup.Set) SearchIDLookup.TryAdd(identifier, platform);
    }

    /// <summary>The registered platform that owns <paramref name="identifier" /> (e.g. <c>"audio://"</c>), if any.</summary>
    public Platform? PlatformFor(string identifier)
    {
        return SearchIDLookup.TryGetValue(identifier.AsSpan(), out var platform) ? platform : null;
    }

    /// <returns>The result, or <c>null</c> when no platform claims the ID or the lookup fails.</returns>
    public Task<PlatformResult?> SearchID(string id, CancellationToken cancellationToken = default)
    {
        Logger.Information("Searching for ID: {ID}", id);
        var normalizedId = id.Trim();
        var separator = normalizedId.IndexOf("://", StringComparison.Ordinal);
        if (separator < 1)
        {
            Logger.Warning("ID does not contain a platform protocol: {ID}", normalizedId);
            return Task.FromResult<PlatformResult?>(null);
        }

        var identifier = normalizedId[..(separator + 3)];
        if (SearchIDLookup.TryGetValue(identifier.AsSpan(), out var platform))
            return platform.GetByIdAsync(normalizedId[(separator + 3)..], cancellationToken);

        Logger.Warning("No platform found for identifier: {Identifier}", identifier);
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

        foreach (var platform in Platforms.OfType<ISupportsPlaylist>())
        await foreach (var result in platform.SearchPlaylist(query, cancellationToken)
                           .Guarded(Logger, platform.GetType().Name, cancellationToken))
        {
            totalResults++;
            yield return result;
        }

        Logger.Debug("Playlist search for {Query} finished with {Count} results", query, totalResults);
    }

    /// <summary>
    ///     Fans <c>/classify</c> out across every HTTP platform pod. First claim wins; nobody claiming
    ///     means an ordinary keyword search, which is the one classification rule that stays in Gaida.
    /// </summary>
    public async Task<ClassifyClaim> ClassifyAsync(string query, CancellationToken cancellationToken = default)
    {
        var trimmed = query.Trim();
        foreach (var platform in Platforms.OfType<HttpPlatform>())
        {
            var claim = await platform.ClassifyAsync(trimmed, cancellationToken);
            if (claim is not null) return claim.Value;
        }

        return new ClassifyClaim(QueryType.Keywords, trimmed, null);
    }
}