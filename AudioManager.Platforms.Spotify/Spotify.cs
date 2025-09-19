namespace AudioManager.Platforms.Spotify;

public class Spotify : Platform
{
    protected override HashSet<string> SearchIDIdentifiers { get; } = ["spotify://"];
    protected override HashSet<string> PlatformDomains { get; } = ["spotify.com"];
    protected override HashSet<string> SearchPlaylistIdentifiers { get; } = ["spotify-playlist://"];
    public override string Name => "Spotify";
    public override string Description => "The Spotify music streaming platform.";
    public override int Priority => 20;

    protected override List<SearchProvider> SearchProviders { get; set; } =
    [
        new SpotifySearchProvider()
    ];
    protected override List<ContentGetter> ContentDownloaders { get; set; } = [];
}