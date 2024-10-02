using AudioManager.Platforms.Cross_Platform;

namespace AudioManager.Platforms.YouTube;

public class YouTube : Platform
{
    public override string Name => "YouTube";
    public override string Description => "The YouTube video platform.";
    public override int Priority => 50;
    
    public override bool SupportsID => true;
    public override bool SupportsSearch => true;

    protected override List<SearchProvider> SearchProviders { get; set; } = [
        new YouTubeSearchProvider_Explode()
    ];

    protected override List<ContentDownloader> ContentDownloaders { get; set; } = [
        new Downloader_YtDLP()
    ];
}