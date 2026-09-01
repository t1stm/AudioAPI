using System.Text.RegularExpressions;

namespace Gaida.API.Contracts;

public enum ParsedQueryKind
{
    Local,
    YouTubeVideo,
    YouTubePlaylist,
    Search,
    Invalid
}

public sealed record ParsedQuery(ParsedQueryKind Kind, string Query, string? PlaylistId = null,
    string? ErrorMessage = null);

/// <summary>Recognises the input forms accepted by the public query resolver.</summary>
public static partial class QueryParser
{
    public static ParsedQuery Parse(string? value)
    {
        var query = value?.Trim();
        if (string.IsNullOrEmpty(query))
            return new ParsedQuery(ParsedQueryKind.Invalid, string.Empty, ErrorMessage: "Query is required.");

        if (query.StartsWith("audio://", StringComparison.OrdinalIgnoreCase))
            return query.Length == "audio://".Length || string.IsNullOrWhiteSpace(query["audio://".Length..])
                ? Invalid(query, "An audio:// query must include an ID.")
                : new ParsedQuery(ParsedQueryKind.Local, "audio://" + query["audio://".Length..]);

        if (query.StartsWith("yt://", StringComparison.OrdinalIgnoreCase))
            return ParseVideoId(query["yt://".Length..], query);

        if (query.StartsWith("yt-playlist://", StringComparison.OrdinalIgnoreCase))
            return ParsePlaylistId(query["yt-playlist://".Length..], query);

        if (Uri.TryCreate(query, UriKind.Absolute, out var uri))
            return ParseUrl(uri, query);

        if (VideoIdRegex().IsMatch(query))
            return ParseVideoId(query, query);

        if (LooksLikePlaylistId(query))
            return ParsePlaylistId(query, query);

        // A non-URL value is an ordinary text search, including Cyrillic text.
        return new ParsedQuery(ParsedQueryKind.Search, query);
    }

    private static ParsedQuery ParseUrl(Uri uri, string originalQuery)
    {
        if (!IsYouTubeHost(uri.Host))
            return Invalid(originalQuery, "Only YouTube URLs are supported.");

        var parameters = ParseQuery(uri.Query);
        if (parameters.TryGetValue("list", out var playlistId))
            return ParsePlaylistId(playlistId, originalQuery);

        var pathParts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var videoId = uri.Host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase)
            ? pathParts.FirstOrDefault()
            : uri.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase)
                ? parameters.GetValueOrDefault("v")
                : pathParts.Length >= 2 && (pathParts[0] is "shorts" or "embed" or "live")
                    ? pathParts[1]
                    : null;

        return videoId is null
            ? Invalid(originalQuery, "The YouTube URL does not contain a video or playlist ID.")
            : ParseVideoId(videoId, originalQuery);
    }

    private static ParsedQuery ParseVideoId(string id, string originalQuery)
    {
        return VideoIdRegex().IsMatch(id)
            ? new ParsedQuery(ParsedQueryKind.YouTubeVideo, "yt://" + id)
            : Invalid(originalQuery, "The YouTube video ID is invalid.");
    }

    private static ParsedQuery ParsePlaylistId(string id, string originalQuery)
    {
        return PlaylistIdRegex().IsMatch(id)
            ? new ParsedQuery(ParsedQueryKind.YouTubePlaylist,
                $"https://www.youtube.com/playlist?list={Uri.EscapeDataString(id)}", id)
            : Invalid(originalQuery, "The YouTube playlist ID is invalid.");
    }

    private static bool IsYouTubeHost(string host)
    {
        return host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".youtu.be", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePlaylistId(string value)
    {
        return value.Length is >= 10 and <= 128 && PlaylistIdRegex().IsMatch(value) &&
               (value.StartsWith("PL", StringComparison.Ordinal) || value.StartsWith("UU", StringComparison.Ordinal) ||
                value.StartsWith("LL", StringComparison.Ordinal) || value.StartsWith("RD", StringComparison.Ordinal) ||
                value.StartsWith("FL", StringComparison.Ordinal) || value.StartsWith("WL", StringComparison.Ordinal) ||
                value.StartsWith("OLAK5uy_", StringComparison.Ordinal));
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => Uri.UnescapeDataString(parts[0]), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => Uri.UnescapeDataString(group.First()[1]), StringComparer.OrdinalIgnoreCase);
    }

    private static ParsedQuery Invalid(string query, string message) =>
        new(ParsedQueryKind.Invalid, query, ErrorMessage: message);

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex VideoIdRegex();

    [GeneratedRegex("^[A-Za-z0-9_-]{10,128}$")]
    private static partial Regex PlaylistIdRegex();
}
