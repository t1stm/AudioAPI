namespace AudioManager.Platforms.Local;

public class MusicResult : PlatformResult
{
    public required string Path { get; set; }
    public override string GetDownloadUrl()
    {
        return ID;
    }
}