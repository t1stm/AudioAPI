using AudioManager.Platforms.Optional;

namespace AudioManager.Platforms.YouTube;

public class YouTubeResult : PlatformResult, HasThumbnail
{
    public string ThumbnailUrl => "";
}