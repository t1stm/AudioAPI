using System.Globalization;
using System.Text.Json.Serialization;
using Gaida.Core.Platforms;

namespace Gaida.API.Contracts;

/// <summary>The public result shape shared by every discovery endpoint.</summary>
public sealed record SearchResultDto(
    string Id,
    string Name,
    string Artist,
    string? Album,
    string ContentUrl,
    string Duration,
    string? ThumbnailUrl,
    string? OriginalTitle,
    string? OriginalArtist);

/// <summary>What the local library has to say about a YouTube result the roll landed on.</summary>
/// <param name="Match"><c>same</c>, <c>variant</c> (a tagged upload, a plain library copy) or <c>weak</c>.</param>
/// <param name="DurationDeltaSeconds">Library minus upload. Reported, never a reason to reject a strong match.</param>
public sealed record LocalVariantDto(
    string Match,
    double Score,
    int DurationDeltaSeconds,
    IReadOnlyList<string> YouTubeTags,
    IReadOnlyList<string> LibraryTags,
    SearchResultDto Result);

/// <summary>A subfolder of the library tree.</summary>
/// <param name="Songs">Songs anywhere beneath this folder, not only directly in it.</param>
public sealed record BrowseFolderDto(string Name, string Path, int Songs);

/// <summary>One level of the library tree: what is directly inside <paramref name="Path" />.</summary>
public sealed record BrowseDto(
    string Path,
    IReadOnlyList<BrowseFolderDto> Folders,
    IReadOnlyList<SearchResultDto> Files);

public sealed record ApiErrorBody(ApiError Error);

public sealed record ApiError(string Code, string Message);

public sealed record QueryResolutionDto
{
    public required string Kind { get; init; }
    public required string Query { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlaylistId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SearchResultDto? Result { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SearchResultDto>? Results { get; init; }
}

public static class DiscoveryResultMapper
{
    /// <summary>Maps a platform result without allowing platform-specific fields to leak into the API contract.</summary>
    public static SearchResultDto? Map(PlatformResult result, HttpRequest request, IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(result.ID)) return null;

        var publicBaseUrl = GetPublicBaseUrl(request, configuration, environment);
        var contentUrl = $"{publicBaseUrl}/Audio/DownloadRaw?id={Uri.EscapeDataString(result.ID)}";

        // The untransliterated title/artist is the display value when the platform has one; the romanized
        // form no longer goes out on the wire in that case — it stays server-side, for matching only
        // (Romanize.cs, TitleNormalizer.cs, LevenshteinDistance.cs).
        var name = result.OriginalTitle is { Length: > 0 } title
            ? title
            : string.IsNullOrWhiteSpace(result.Name)
                ? "Unknown title"
                : result.Name;
        var artist = result.OriginalArtist is { Length: > 0 } artistName
            ? artistName
            : string.IsNullOrWhiteSpace(result.Artist)
                ? "Unknown artist"
                : result.Artist;

        return new SearchResultDto(
            result.ID,
            name,
            artist,
            result.Album,
            contentUrl,
            FormatDuration(result.Duration),
            result.ThumbnailUrl,
            result.OriginalTitle,
            result.OriginalArtist);
    }

    /// <summary>
    ///     Whole seconds, <c>hh:mm:ss</c> — the shape API.md documents. TimeSpan's "c" format appends a
    ///     seven-digit fractional part whenever the duration has one, so a track measured off a real file
    ///     went out as <c>00:03:22.1660000</c> where the contract promised <c>00:03:22</c>. Truncating
    ///     rather than reformatting keeps "c"'s day handling for anything over 24 hours.
    /// </summary>
    private static string FormatDuration(TimeSpan duration)
    {
        var clamped = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return TimeSpan.FromSeconds(Math.Floor(clamped.TotalSeconds))
            .ToString("c", CultureInfo.InvariantCulture);
    }

    private static string GetPublicBaseUrl(HttpRequest request, IConfiguration configuration,
        IHostEnvironment environment)
    {
        var configuredUrl = environment.IsDevelopment() ? null : configuration["PublicApiBaseUrl"];
        return Uri.TryCreate(configuredUrl, UriKind.Absolute, out var publicUri)
            ? publicUri.GetLeftPart(UriPartial.Authority).TrimEnd('/')
            : $"{request.Scheme}://{request.Host.Value}".TrimEnd('/');
    }
}