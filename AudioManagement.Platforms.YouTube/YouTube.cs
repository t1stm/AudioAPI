using System.Text.RegularExpressions;
using AudioManagement.Platforms.Cross_Platform;
using AudioManagement.Platforms.Errors;
using AudioManagement.Platforms.Optional.Supports;
using AudioManagement.Platforms.YouTube.Cache;
using AudioManagement.Platforms.YouTube.Getters;
using AudioManagement.Platforms.YouTube.Search_Providers;
using Result;
using Result.Objects;

namespace AudioManagement.Platforms.YouTube;

public sealed partial class YouTube : Platform, ISupportsSearch, ISupportsPlaylist
{
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
        new YouTubeSearchProviderCached(YouTubeCacher),
        new YouTubeSearchProviderMadeyoga(),
        new YouTubeSearchProviderExplode()
    ];

    protected override List<ContentGetter> ContentDownloaders { get; set; } =
    [
        new GetterLocalCache(),
        new GetterYouTubeExplode(),
        new GetterYtDlp(),
        new GetterVideoLibrary()
    ];

    public async Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchPlaylist(string playlist,
        CancellationToken cancellationToken = default)
    {
        foreach (var searchProvider in
                 SearchProviders.Where(searchProvider => searchProvider is ISupportsPlaylist)
                     .Cast<ISupportsPlaylist>())
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
                 SearchProviders.Where(searchProvider => searchProvider is ISupportsSearch)
                     .Cast<ISupportsSearch>())
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
        await YouTubeCacher.AddToCacheAsync(results.GetOk()
            .Where(r => r is YouTubeResult)
            .Cast<YouTubeResult>());
    }

    [GeneratedRegex(@"\/playlist\?list=[a-zA-Z0-9_-]+")]
    private static partial Regex PlaylistRegex();
}