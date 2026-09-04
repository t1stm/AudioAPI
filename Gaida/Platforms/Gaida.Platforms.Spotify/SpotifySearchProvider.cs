using System.Runtime.CompilerServices;
using Gaida.Core.Platforms;
using Gaida.Core.Platforms.Optional.Supports;
using Serilog;
using SpotifyAPI.Web;

namespace Gaida.Platforms.Spotify;

/// <summary>
///     Spotify hands out metadata, never audio: <see cref="SpotifyResult" /> has no downloader and its
///     download URL is empty. Everything produced here is a name for something else to find — see the
///     resolver in Gaida.API.
/// </summary>
public class SpotifySearchProvider(ILogger logger) : SearchProvider(logger), ISupportsID, ISupportsPlaylist
{
    private static readonly Lazy<SpotifyClient?> Spotify = new(CreateClient);
    public override string PlatformIdentifier => "spotify://";
    public override int Priority => 99;

    public async Task<PlatformResult?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (Client() is not { } client) return null;

        var track = await Ask(() => client.Tracks.Get(id, cancellationToken), $"track {id}");
        return track is null ? null : ToResult(track);
    }

    /// <summary>Playlist entries as Spotify pages them — 100 per request, so a long one is several.</summary>
    public async IAsyncEnumerable<PlatformResult> SearchPlaylist(string playlist,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Client() is not { } client) yield break;

        var playlistId = PlaylistId(playlist);
        if (string.IsNullOrWhiteSpace(playlistId)) yield break;

        var firstPage = await Ask(() => client.Playlists.GetPlaylistItems(playlistId, cancellationToken),
            $"playlist {playlistId}");
        if (firstPage is null) yield break;

        await foreach (var item in client.Paginate(firstPage, cancel: cancellationToken))
            if (item.Track is FullTrack track)
                yield return ToResult(track);
    }

    /// <summary>The playlist ID out of a <c>spotify-playlist://</c> id, a Spotify URI or an open.spotify.com URL.</summary>
    public static string PlaylistId(string playlist)
    {
        var value = playlist.Trim();
        if (value.StartsWith("spotify-playlist://", StringComparison.OrdinalIgnoreCase))
            return value["spotify-playlist://".Length..];

        if (value.StartsWith("spotify:playlist:", StringComparison.OrdinalIgnoreCase))
            return value["spotify:playlist:".Length..];

        var marker = value.IndexOf("/playlist/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return string.Empty;

        var id = value[(marker + "/playlist/".Length)..];
        var end = id.IndexOfAny(['?', '/', '#']);
        return end < 0 ? id : id[..end];
    }

    private SpotifyResult ToResult(FullTrack track)
    {
        return new SpotifyResult
        {
            ID = PlatformIdentifier + track.Id,
            Downloaders = [],
            Name = track.Name,
            Artist = string.Join(", ", track.Artists.Select(artist => artist.Name)),
            Duration = TimeSpan.FromMilliseconds(track.DurationMs),
            Album = track.Album?.Name,
            // Spotify orders its images largest first.
            ThumbnailUrl = track.Album?.Images?.FirstOrDefault()?.Url
        };
    }

    /// <summary>
    ///     Runs one Spotify call, turning a refusal into <c>null</c> and a log line that names the status.
    ///     APIException's own message is empty, so an unwrapped one reaches the log as a stack trace saying
    ///     nothing — which is exactly what a 403 "Active premium subscription required for the owner of the
    ///     app" looked like from here.
    /// </summary>
    private async Task<T?> Ask<T>(Func<Task<T>> call, string what) where T : class
    {
        try
        {
            return await call();
        }
        catch (APIException e)
        {
            Logger.Warning("Spotify refused {What}: {Status} {Message}", what,
                e.Response?.StatusCode, e.Response?.Body ?? e.Message);
            return null;
        }
    }

    private SpotifyClient? Client()
    {
        if (Spotify.Value is { } client) return client;

        Logger.Warning("SPOTIFY_ID / SPOTIFY_SECRET are not set, skipping Spotify");
        return null;
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
