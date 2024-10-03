using AudioManager.Platforms.Cross_Platform;

namespace AudioManager.Platforms.YouTube;

public sealed class YouTube : Platform
{
    public override string[] SearchIDIdentifiers => ["yt://"];
    public override string[] SearchPlaylistIdentifiers => ["yt-playlist://"];
    public override string Name => "YouTube";
    public override string Description => "The YouTube video and music platform.";
    public override int Priority => 50;
    
    public override bool SupportsID => true;
    public override bool SupportsSearch => true;
    public override bool SupportsPlaylists => true;
    public override bool SupportsPagination => true;

    protected override List<SearchProvider> SearchProviders { get; set; } = [
        new YouTubeSearchProvider_Explode()
    ];

    protected override List<ContentDownloader> ContentDownloaders { get; set; } = [
        new Downloader_YtDLP()
    ];

    public override void Initialize()
    {
        foreach (var search_provider in SearchProviders)
        {
            search_provider.RegisterContentDownloaders(ContentDownloaders);
        }
        base.Initialize();
    }
}