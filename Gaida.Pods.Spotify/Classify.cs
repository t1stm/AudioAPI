namespace Gaida.Pods.Spotify;

/// <summary>
///     Outcome of classifying one raw query, mirroring the YouTube pod's <c>Classify</c>: 200 (recognised),
///     400 (ours but malformed) or 404 (not ours -- Gaida.API defaults that to a keyword search).
/// </summary>
public readonly record struct ClassifyResult(int Status, string? Kind, string? Id, string? Error);

/// <summary>
///     Recognises <c>spotify://</c> / <c>spotify-playlist://</c> ids, <c>spotify:track:…</c> URIs and
///     open.spotify.com track and playlist links. Pure string parsing -- no network, no credentials needed,
///     so a pod without SPOTIFY_ID still classifies correctly and simply finds nothing afterwards.
/// </summary>
public static class Classify
{
    private static readonly ClassifyResult NotMine = new(404, null, null, null);

    public static ClassifyResult Parse(string? value)
    {
        var query = value?.Trim();
        if (string.IsNullOrEmpty(query)) return NotMine;

        if (query.StartsWith("spotify://", StringComparison.OrdinalIgnoreCase))
            return Track(query["spotify://".Length..]);

        if (query.StartsWith("spotify-playlist://", StringComparison.OrdinalIgnoreCase))
            return Playlist(query["spotify-playlist://".Length..]);

        // The URI form the desktop client copies: spotify:track:ID / spotify:playlist:ID.
        if (query.StartsWith("spotify:track:", StringComparison.OrdinalIgnoreCase))
            return Track(query["spotify:track:".Length..]);

        if (query.StartsWith("spotify:playlist:", StringComparison.OrdinalIgnoreCase))
            return Playlist(query["spotify:playlist:".Length..]);

        if (!Uri.TryCreate(query, UriKind.Absolute, out var uri) || !IsSpotifyHost(uri.Host)) return NotMine;

        // Locale-prefixed links are ordinary: open.spotify.com/intl-de/track/ID.
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < parts.Length - 1; index++)
            switch (parts[index].ToLowerInvariant())
            {
                case "track": return Track(parts[index + 1]);
                case "playlist": return Playlist(parts[index + 1]);
            }

        return Invalid("The Spotify link is not a track or a playlist.");
    }

    private static ClassifyResult Track(string id)
    {
        return IsId(id) ? new ClassifyResult(200, "id", "spotify://" + id, null) : Invalid("The Spotify track ID is invalid.");
    }

    private static ClassifyResult Playlist(string id)
    {
        return IsId(id)
            ? new ClassifyResult(200, "playlist", "spotify-playlist://" + id, null)
            : Invalid("The Spotify playlist ID is invalid.");
    }

    /// <summary>Spotify's base-62 IDs are 22 characters; the length is not promised, so only the alphabet is enforced.</summary>
    private static bool IsId(string value)
    {
        return value.Length is >= 10 and <= 64 && value.All(char.IsAsciiLetterOrDigit);
    }

    private static bool IsSpotifyHost(string host)
    {
        return host.Equals("spotify.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".spotify.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("spotify.link", StringComparison.OrdinalIgnoreCase);
    }

    private static ClassifyResult Invalid(string message)
    {
        return new ClassifyResult(400, null, null, message);
    }
}
