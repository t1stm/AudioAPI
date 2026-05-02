using System.Text.RegularExpressions;
using AudioManagement.Platforms.Cross_Platform;
using AudioManagement.Platforms.Errors;
using AudioManagement.Platforms.Optional.Supports;
using AudioManagement.Platforms.YouTube.Cache;
using AudioManagement.Platforms.YouTube.Getters;
using AudioManagement.Platforms.YouTube.Search_Providers;
using Result;
using Result.Objects;
using Serilog;

namespace AudioManagement.Platforms.YouTube;

public sealed partial class YouTube(ILogger logger) : Platform(logger), IPlatformFactory<YouTube>, ISupportsSearch, ISupportsPlaylist
{
    public static YouTube CreateNew(ILogger logger)
    {
        return new YouTube(logger);
    }
    
    public static readonly YouTubeCacher YouTubeCacher = new();
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
        new YouTubeSearchProviderCached(logger, YouTubeCacher),
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

    private static async Task PopulateYouTubeCache(Result<IEnumerable<PlatformResult>, SearchError> results)
    {
        if (results == Status.Error) return;
        await YouTubeCacher.AddToCacheAsync(results.GetOk().OfType<YouTubeResult>());
    }

    [GeneratedRegex(@"\/playlist\?list=[a-zA-Z0-9_-]+")]
    private static partial Regex PlaylistRegex();
}