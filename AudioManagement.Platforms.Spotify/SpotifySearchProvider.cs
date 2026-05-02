using AudioManagement.Platforms.Errors;
using AudioManagement.Platforms.Optional.Supports;
using Result;
using SpotifyAPI.Web;

namespace AudioManagement.Platforms.Spotify;

public class SpotifySearchProvider : SearchProvider, ISupportsID
{
    private static readonly SpotifyClientConfig SpotifyConfig = SpotifyClientConfig
        .CreateDefault()
        .WithAuthenticator(new ClientCredentialsAuthenticator
        (Environment.GetEnvironmentVariable("SPOTIFY_ID") ??
         throw new ArgumentNullException(nameof(SpotifyConfig), "Environment variable SPOTIFY_ID is not set"),
            Environment.GetEnvironmentVariable("SPOTIFY_SECRET") ??
            throw new ArgumentNullException(nameof(SpotifyConfig), "Environment variable SPOTIFY_SECRET is not set")));

    private static readonly Lazy<SpotifyClient> Spotify = new(() => new SpotifyClient(SpotifyConfig));
    public override string Name => "Spotify";
    public override string PlatformIdentifier => "spotify://";
    public override int Priority => 99;

    public async Task<Result<PlatformResult, SearchError>> TryID(string id,
        CancellationToken cancellationToken = default)
    {
        var track = await Spotify.Value.Tracks.Get(id, cancellationToken);
        var result = new SpotifyResult
        {
            ID = track.Id,
            Downloaders = [],
            Name = track.Name,
            Artist = ArtistsNameCombine(track.Artists),
            Duration = TimeSpan.FromMilliseconds(track.DurationMs),
            Album = track.Album.Name,
            Explicit = track.Explicit
        };

        return Result<PlatformResult, SearchError>.Success(result);
    }

    protected static string ArtistsNameCombine(List<SimpleArtist> artists)
    {
        var artist = "";
        for (var index = 0; index < artists.Count; index++)
        {
            var simpleArtist = artists[index];
            artist += $"{index switch { 0 => "", _ => ", " }}{simpleArtist.Name}";
        }

        return artist;
    }
}