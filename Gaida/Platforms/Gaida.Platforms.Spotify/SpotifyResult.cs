using Gaida.Core.Platforms;
namespace Gaida.Platforms.Spotify;

public class SpotifyResult : PlatformResult
{
    public bool Explicit { get; set; }

    public override string GetDownloadUrl()
    {
        return "";
    }
}