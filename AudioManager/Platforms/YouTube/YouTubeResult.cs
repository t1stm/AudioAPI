using AudioManager.Platforms.Optional;

namespace AudioManager.Platforms.YouTube;

public sealed class YouTubeResult : PlatformResult, HasThumbnail
{
    public string? ThumbnailUrl { get; init; }
    public override string GetDownloadUrl()
    {
        var pure_id = ID.Split("://")[1..];
        return $"https://www.youtube.com/watch?v={ID}";
    }
}