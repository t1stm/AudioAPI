using AudioManager.Platforms.Optional;

namespace AudioManager.Platforms.YouTube;

public sealed class YouTubeResult : PlatformResult
{
    public override string GetDownloadUrl()
    {
        var pure_id = ID.Split("://")[1];
        return $"https://www.youtube.com/watch?v={pure_id}";
    }
}