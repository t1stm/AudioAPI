using Gaida.Core.Platforms;
using Serilog;

namespace Gaida.Platforms.Spotify;

public class Spotify : Platform
{
    public Spotify(ILogger logger) : base(logger)
    {
        SearchProviders = [new SpotifySearchProvider(logger)];
        ContentDownloaders = [];
    }

    protected override HashSet<string> SearchIDIdentifiers { get; } = ["spotify://"];
    protected override HashSet<string> SearchPlaylistIdentifiers { get; } = ["spotify-playlist://"];

    protected override List<SearchProvider> SearchProviders { get; set; }
    protected override List<ContentGetter> ContentDownloaders { get; set; }
}
