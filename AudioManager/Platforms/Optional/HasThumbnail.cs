using System.Text.Json.Serialization;

namespace AudioManager.Platforms.Optional;

public interface HasThumbnail
{
    [JsonInclude]
    public string? ThumbnailUrl { get; }
}