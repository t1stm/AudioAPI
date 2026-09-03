using Gaida.Core.Platforms;
using Gaida.Core.Platforms.Optional.Supports;
using Serilog;
using SpotifyAPI.Web;

namespace Gaida.Platforms.Spotify;

public class SpotifySearchProvider(ILogger logger) : SearchProvider(logger), ISupportsID
{
    private static readonly Lazy<SpotifyClient?> Spotify = new(CreateClient);
    public override string PlatformIdentifier => "spotify://";
    public override int Priority => 99;

    public async Task<PlatformResult?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (Spotify.Value is not { } client)
        {
            Logger.Warning("SPOTIFY_ID / SPOTIFY_SECRET are not set, skipping Spotify");
            return null;
        }

        var track = await client.Tracks.Get(id, cancellationToken);
        return new SpotifyResult
        {
            ID = PlatformIdentifier + track.Id,
            Downloaders = [],
            Name = track.Name,
            Artist = string.Join(", ", track.Artists.Select(a => a.Name)),
            Duration = TimeSpan.FromMilliseconds(track.DurationMs),
            Album = track.Album.Name
        };
    }

    private static SpotifyClient? CreateClient()
    {
        var id = Environment.GetEnvironmentVariable("SPOTIFY_ID");
        var secret = Environment.GetEnvironmentVariable("SPOTIFY_SECRET");
        if (id is null || secret is null) return null;

        return new SpotifyClient(SpotifyClientConfig.CreateDefault()
            .WithAuthenticator(new ClientCredentialsAuthenticator(id, secret)));
    }
}