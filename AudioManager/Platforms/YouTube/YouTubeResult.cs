using AudioManager.Platforms.Optional;

namespace AudioManager.Platforms.YouTube;

public sealed class YouTubeResult : PlatformResult, HasThumbnail
{
    public string? ThumbnailUrl { get; init; }
    public override string GetDownloadUrl()
    {
        return $"https://www.youtube.com/watch?v={ID}";
    }
}