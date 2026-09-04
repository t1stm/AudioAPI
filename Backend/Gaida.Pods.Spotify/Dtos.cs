using System.Globalization;
using Gaida.Core.Platforms;

namespace Gaida.Pods.Spotify;

/// <summary>
///     The pod result shape, identical to the other pods'. No <c>contentUrl</c> and, for this platform, no
///     content at all: these are names for Gaida.API's resolver to look up somewhere playable.
/// </summary>
public sealed record ResultDto(
    string Id,
    string Name,
    string Artist,
    string? Album,
    string Duration,
    string? ThumbnailUrl,
    string? OriginalTitle,
    string? OriginalArtist);

public sealed record ClassifyDto(string? Kind, string? Id, string? Error);

public static class ResultMapper
{
    public static ResultDto? Map(PlatformResult result)
    {
        if (string.IsNullOrWhiteSpace(result.ID)) return null;

        return new ResultDto(
            result.ID,
            string.IsNullOrWhiteSpace(result.Name) ? "Unknown title" : result.Name,
            string.IsNullOrWhiteSpace(result.Artist) ? "Unknown artist" : result.Artist,
            result.Album,
            (result.Duration < TimeSpan.Zero ? TimeSpan.Zero : result.Duration)
            .ToString("c", CultureInfo.InvariantCulture),
            result.ThumbnailUrl,
            result.OriginalTitle,
            result.OriginalArtist);
    }
}
