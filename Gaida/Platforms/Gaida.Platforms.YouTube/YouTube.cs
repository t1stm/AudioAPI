using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Gaida.Core.Platforms;
using Gaida.Core.Platforms.Cross_Platform;
using Gaida.Core.Platforms.Optional.Supports;
using Gaida.Core.Utils;
using Gaida.Platforms.YouTube.Cache;
using Gaida.Platforms.YouTube.Getters;
using Gaida.Platforms.YouTube.Search_Providers;
using Serilog;

namespace Gaida.Platforms.YouTube;

public sealed partial class YouTube : Platform, ISupportsSearch, ISupportsPlaylist
{
    private readonly YouTubeCacher _cacher;

    public YouTube(ILogger logger) : base(logger)
    {
        _cacher = new YouTubeCacher(logger);

        SearchProviders =
        [
            new YouTubeSearchProviderCached(logger, _cacher),
            new YouTubeSearchProviderExplode(logger)
        ];

        ContentDownloaders =
        [
            new GetterLocalCache(logger),
            new GetterYouTubeExplode(logger),
            new GetterYtDlp(logger)
        ];
    }

    protected override HashSet<string> SearchIDIdentifiers => ["yt://"];
    protected override HashSet<string> SearchPlaylistIdentifiers => ["yt-playlist://"];

    protected override List<SearchProvider> SearchProviders { get; set; }
    protected override List<ContentGetter> ContentDownloaders { get; set; }

    public async IAsyncEnumerable<PlatformResult> SearchPlaylist(string playlist,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Logger.Debug("Searching for playlist: {Playlist}", playlist);
        await foreach (var result in FirstProviderWithResults<ISupportsPlaylist>(
                           (provider, token) => provider.SearchPlaylist(playlist, token), cancellationToken))
            yield return result;
    }

    public async IAsyncEnumerable<PlatformResult> SearchKeywords(string keywords,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Logger.Debug("Searching for keywords: {Keywords}", keywords);
        await foreach (var result in FirstProviderWithResults<ISupportsSearch>(
                           (provider, token) => provider.SearchKeywords(keywords, token), cancellationToken))
            yield return result;
    }

    public override bool IsPlaylistUrl(ReadOnlySpan<char> query)
    {
        return PlaylistRegex().IsMatch(query);
    }

    /// <summary>
    ///     Streams results from the highest priority provider that returns any, caching what it saw on the way out.
    /// </summary>
    private async IAsyncEnumerable<PlatformResult> FirstProviderWithResults<T>(
        Func<T, CancellationToken, IAsyncEnumerable<PlatformResult>> search,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var provider in SearchProviders.OfType<T>())
        {
            var found = new List<YouTubeResult>();

            await foreach (var result in search(provider, cancellationToken)
                               .Guarded(Logger, provider!.GetType().Name, cancellationToken))
            {
                if (result is YouTubeResult youTubeResult) found.Add(youTubeResult);
                yield return result;
            }

            if (found.Count == 0) continue;

            await _cacher.AddToCacheAsync(found);
            yield break;
        }
    }

    [GeneratedRegex(@"\/playlist\?list=[a-zA-Z0-9_-]+")]
    private static partial Regex PlaylistRegex();
}
