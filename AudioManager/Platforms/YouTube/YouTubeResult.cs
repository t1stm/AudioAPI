namespace AudioManager.Platforms.YouTube;

public sealed class YouTubeResult : PlatformResult
{
    public string PureId => ID.Split("://")[1];
    
    public override string GetDownloadUrl()
    {
        return $"https://www.youtube.com/watch?v={PureId}";
    }
}