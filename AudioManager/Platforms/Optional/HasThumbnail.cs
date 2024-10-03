using System.Text.Json.Serialization;

namespace AudioManager.Platforms.Optional;

public interface HasThumbnail
{
    [JsonIgnore]
    public string? ThumbnailUrl { get; }
}