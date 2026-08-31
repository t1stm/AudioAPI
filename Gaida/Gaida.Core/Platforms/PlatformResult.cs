using System.Text.Json.Serialization;
using Gaida.Core.Streams;

namespace Gaida.Core.Platforms;

public abstract class PlatformResult
{
    [JsonIgnore] public required IReadOnlyList<ContentGetter> Downloaders = [];
    [JsonInclude] public required string ID;
    [JsonInclude] public string? Name { get; set; }
    [JsonInclude] public string? Artist { get; set; }
    [JsonInclude] public string? Album { get; set; }
    [JsonInclude] public TimeSpan Duration { get; set; }
    [JsonInclude] public string? ThumbnailUrl { get; set; }

    /// <summary>Untransliterated title, when the platform has one.</summary>
    [JsonInclude] public string? OriginalTitle { get; set; }

    /// <summary>Untransliterated artist, when the platform has one.</summary>
    [JsonInclude] public string? OriginalArtist { get; set; }

    public abstract string GetDownloadUrl();

    public virtual ReadOnlySpan<char> GetPureID()
    {
        var span = ID.AsSpan();
        Span<Range> ranges = stackalloc Range[2];

        var count = span.Split(ranges, "://");
        return count > 1 ? span[ranges[1]] : span;
    }

    /// <returns>The content stream, or <c>null</c> when no downloader could provide it.</returns>
    public virtual async Task<StreamSpreader?> GetContentDataAsync(CancellationToken token = default)
    {
        foreach (var downloader in Downloaders)
        {
            var result = await downloader.GetContentDataAsync(this, token);
            if (result is not null) return result;
        }

        return null;
    }
}
