using System.Text.RegularExpressions;
using Gaida.Core.Platforms.Cross_Platform;
using Gaida.Core.Platforms.Errors;
using Gaida.Core.Platforms.Optional.Supports;
using Gaida.Platforms.YouTube.Cache;
using Gaida.Platforms.YouTube.Getters;
using Gaida.Platforms.YouTube.Search_Providers;
using Result;
using Result.Objects;
using Serilog;
using Gaida.Core.Platforms;

namespace Gaida.Platforms.YouTube;

public sealed partial class YouTube(ILogger logger) : Platform(logger), IPlatformFactory<YouTube>, ISupportsSearch, ISupportsPlaylist
{
    public static YouTube CreateNew(ILogger logger)
    {
        return new YouTube(logger);
    }

    private static YouTubeCacher? _youTubeCacher;
    protected override HashSet<string> SearchIDIdentifiers => ["yt://"];
    protected override HashSet<string> SearchPlaylistIdentifiers => ["yt-playlist://"];

    protected override HashSet<string> PlatformDomains =>
    [
        "youtube.com", "youtu.be",
        "m.youtube.com", "music.youtube.com"
    ];

    public override string Name => "YouTube";
    public override string Description => "The YouTube video and music platform.";
    public override int Priority => 50;

    protected override List<SearchProvider> SearchProviders { get; set; } =
    [
        new YouTubeSearchProviderCached(logger, _youTubeCacher ??= new YouTubeCacher(logger)),
        new YouTubeSearchProviderMadeyoga(logger),
        new YouTubeSearchProviderExplode(logger)
    ];

    protected override List<ContentGetter> ContentDownloaders { get; set; } =
    [
        new GetterLocalCache(logger),
        new GetterYouTubeExplode(logger),
        new GetterYtDlp(logger),
        new GetterVideoLibrary(logger)
    ];

    public async Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchPlaylist(string playlist,
        CancellationToken cancellationToken = default)
    {
        Logger.Debug("Searching for playlist: {Playlist}", playlist);
        foreach (var searchProvider in
                 SearchProviders.OfType<ISupportsPlaylist>())
        {
            var result = await searchProvider.TrySearchPlaylist(playlist, cancellationToken);
            _ = PopulateYouTubeCache(result);
            if (result == Status.Ok) return result;
        }

        return Result<IEnumerable<PlatformResult>, SearchError>.Error(default);
    }

    public bool IsPlaylistUrl(ReadOnlySpan<char> query)
    {
        return PlaylistRegex().IsMatch(query);
    }

    public async Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchKeywords(string keywords,
        CancellationToken cancellationToken = default)
    {
        Logger.Debug("Searching for keywords: {Keywords}", keywords);
        foreach (var searchProvider in
                 SearchProviders.OfType<ISupportsSearch>())
        {
            var result = await searchProvider.TrySearchKeywords(keywords, cancellationToken);
            _ = PopulateYouTubeCache(result);
            if (result == Status.Ok) return result;
        }

        return Result<IEnumerable<PlatformResult>, SearchError>.Error(default);
    }

    public override void Initialize()
    {
        foreach (var searchProvider in SearchProviders) searchProvider.RegisterContentDownloaders(ContentDownloaders);
        base.Initialize();
    }

    private async Task PopulateYouTubeCache(Result<IEnumerable<PlatformResult>, SearchError> results)
    {
        if (results == Status.Error)
        {
            Logger.Error("Error while populating YouTube cache: {@Exception}", results.GetError());
            return;
        }
        
        if (_youTubeCacher is not null) // should always be true when this is called
            await _youTubeCacher.AddToCacheAsync(results.GetOk().OfType<YouTubeResult>());
    }

    [GeneratedRegex(@"\/playlist\?list=[a-zA-Z0-9_-]+")]
    private static partial Regex PlaylistRegex();
}