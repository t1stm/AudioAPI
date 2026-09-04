using System.Text.Json.Serialization;
using Gaida.Core.Platforms;

namespace Gaida.Platforms.MusicDatabase;

public class MusicResult : PlatformResult
{
    [JsonIgnore] public string Path { get; set; } = string.Empty;

    public override string GetDownloadUrl()
    {
        return ID;
    }
}