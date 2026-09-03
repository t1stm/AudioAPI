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
    string? ThumbnailUrl);

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

        return new SearchResultDto(
            result.ID,
            string.IsNullOrWhiteSpace(result.Name) ? "Unknown title" : result.Name,
            string.IsNullOrWhiteSpace(result.Artist) ? "Unknown artist" : result.Artist,
            result.Album,
            contentUrl,
            result.Duration < TimeSpan.Zero
                ? TimeSpan.Zero.ToString("c", CultureInfo.InvariantCulture)
                : result.Duration.ToString("c", CultureInfo.InvariantCulture),
            result.ThumbnailUrl);
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