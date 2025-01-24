using System.Text.RegularExpressions;
using AudioManager.Platforms.Cross_Platform;
using AudioManager.Platforms.Errors;
using AudioManager.Platforms.Optional.Supports;
using AudioManager.Platforms.YouTube.Cache;
using AudioManager.Platforms.YouTube.Getters;
using AudioManager.Platforms.YouTube.Search_Providers;
using Result;
using Result.Objects;

namespace AudioManager.Platforms.YouTube;

public sealed partial class YouTube : Platform, ISupportsSearch, ISupportsPlaylist
{
    public static readonly YouTubeCacher YouTubeCacher = new();
    public override HashSet<string> SearchIDIdentifiers => ["yt://"];
    public override HashSet<string> SearchPlaylistIdentifiers => ["yt-playlist://"];
    public override HashSet<string> PlatformDomains => ["youtube.com", "youtu.be", 
        "m.youtube.com", "music.youtube.com"];
    public override string Name => "YouTube";
    public override string Description => "The YouTube video and music platform.";
    public override int Priority => 50;
    protected override List<SearchProvider> SearchProviders { get; set; } = [
        new YouTubeSearchProvider_Cached(YouTubeCacher),
        new YouTubeSearchProvider_Madeyoga(),
        new YouTubeSearchProvider_Explode()
    ];

    protected override List<ContentGetter> ContentDownloaders { get; set; } = [
        new Getter_LocalCache(),
        new Getter_YouTubeExplode(),
        new Getter_YtDLP(),
        new Getter_VideoLibrary()
    ];

    public override void Initialize()
    {
        foreach (var search_provider in SearchProviders)
        {
            search_provider.RegisterContentDownloaders(ContentDownloaders);
        }
        base.Initialize();
    }

    public async Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchKeywords(string keywords,
        CancellationToken cancellation_token = default)
    {
        foreach (var search_provider in 
                 SearchProviders.Where(search_provider => search_provider is ISupportsSearch)
                     .Cast<ISupportsSearch>())
        {
            var result = await search_provider.TrySearchKeywords(keywords, cancellation_token);
            _ = PopulateYouTubeCache(result);
            if (result == Status.OK) return result;
        }

        return Result<IEnumerable<PlatformResult>, SearchError>.Error(default);
    }
    
    public async Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchPlaylist(string playlist,
        CancellationToken cancellation_token = default)
    {
        foreach (var search_provider in 
                 SearchProviders.Where(search_provider => search_provider is ISupportsPlaylist)
                     .Cast<ISupportsPlaylist>())
        {
            var result = await search_provider.TrySearchPlaylist(playlist, cancellation_token);
            _ = PopulateYouTubeCache(result);
            if (result == Status.OK) return result;
        }

        return Result<IEnumerable<PlatformResult>, SearchError>.Error(default);
    }

    public bool IsPlaylistUrl(string query)
    {
        return PlaylistRegex().IsMatch(query);
    }

    private static Task PopulateYouTubeCache(Result<IEnumerable<PlatformResult>, SearchError> results)
    {
        return Task.Run(async () =>
        {
            if (results == Status.Error) return;
            await YouTubeCacher.AddToCacheAsync(results.GetOK()
                .Where(r => r is YouTubeResult)
                .Cast<YouTubeResult>());
        });
    }
    
    [GeneratedRegex(@"\/playlist\?list=[a-zA-Z0-9_-]+")]
    private static partial Regex PlaylistRegex();
}