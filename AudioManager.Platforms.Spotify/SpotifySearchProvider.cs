using AudioManager.Platforms.Errors;
using AudioManager.Platforms.Optional.Supports;
using Result;
using SpotifyAPI.Web;

namespace AudioManager.Platforms.Spotify;

public class SpotifySearchProvider : SearchProvider, ISupportsID
{
    public override string Name => "Spotify";
    public override string PlatformIdentifier => "spotify://";
    public override int Priority => 99;
    
    private static readonly SpotifyClientConfig SpotifyConfig = SpotifyClientConfig
        .CreateDefault()
        .WithAuthenticator(new ClientCredentialsAuthenticator
        (Environment.GetEnvironmentVariable("SPOTIFY_ID") ?? throw new ArgumentNullException(),
            Environment.GetEnvironmentVariable("SPOTIFY_SECRET") ?? throw new ArgumentNullException()));
    
    private static readonly Lazy<SpotifyClient> Spotify = new(() => new SpotifyClient(SpotifyConfig));
    
    protected static string ArtistsNameCombine(List<SimpleArtist> artists)
    {
        var artist = "";
        for (var index = 0; index < artists.Count; index++)
        {
            var simple_artist = artists[index];
            artist += $"{index switch { 0 => "", _ => ", " }}{simple_artist.Name}";
        }

        return artist;
    }

    public async Task<Result<PlatformResult, SearchError>> TryID(string id, CancellationToken cancellation_token = default)
    {
        var track = await Spotify.Value.Tracks.Get(id, cancellation_token);
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
}