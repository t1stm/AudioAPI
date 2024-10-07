namespace AudioManager.Platforms.Local;

public class MusicDatabase : Platform
{
    public override HashSet<string> SearchIDIdentifiers { get; } = ["audio://"];
    public override HashSet<string> PlatformDomains { get; } = [];
    public override HashSet<string> SearchPlaylistIdentifiers { get; } = [];
    
    public override string Name => "Music Database";
    public override string Description => "Locally stored music";
    public override int Priority => 99;
    protected override List<SearchProvider> SearchProviders { get; set; } = [
        new MusicSearchProvider()
    ];
    protected override List<ContentGetter> ContentDownloaders { get; set; } = [
        new MusicGetter()
    ];
}