using Gaida.Core.Platforms;
using Gaida.Core.Platforms.Optional.Supports;
using Serilog;

namespace Gaida.Platforms.Spotify;

/// <summary>
///     Playlists and track links, and neither produces anything playable — this platform has no content
///     getters at all. A Spotify result is a track's name and artist; turning that into audio is the
///     resolver's job in Gaida.API.
///     ponytail: no keyword search. Every keyword query already fans out to platforms that have audio, and
///     a Spotify hit for one would only be another name to resolve into the result they just returned.
/// </summary>
public sealed class Spotify : Platform, ISupportsPlaylist
{
    private readonly SpotifySearchProvider _provider;

    public Spotify(ILogger logger) : base(logger)
    {
        _provider = new SpotifySearchProvider(logger);
        SearchProviders = [_provider];
        ContentDownloaders = [];
    }

    protected override HashSet<string> SearchIDIdentifiers { get; } = ["spotify://"];
    protected override HashSet<string> SearchPlaylistIdentifiers { get; } = ["spotify-playlist://"];

    protected override List<SearchProvider> SearchProviders { get; set; }
    protected override List<ContentGetter> ContentDownloaders { get; set; }

    public IAsyncEnumerable<PlatformResult> SearchPlaylist(string playlist,
        CancellationToken cancellationToken = default)
    {
        return _provider.SearchPlaylist(playlist, cancellationToken);
    }

}
