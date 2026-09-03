using Gaida.Core.Platforms;
using Serilog;

namespace Gaida.Platforms.Spotify;

public sealed class Spotify(ILogger logger) : Platform(logger)
{
    protected override HashSet<string> SearchIDIdentifiers { get; } = ["spotify://"];
    protected override HashSet<string> SearchPlaylistIdentifiers { get; } = ["spotify-playlist://"];

    protected override List<SearchProvider> SearchProviders { get; set; } = [new SpotifySearchProvider(logger)];
    protected override List<ContentGetter> ContentDownloaders { get; set; } = [];
}