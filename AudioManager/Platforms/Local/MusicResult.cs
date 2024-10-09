using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioManager.Platforms.Local;

public class MusicResult : PlatformResult
{
    [JsonIgnore]
    public string Path { get; set; } = string.Empty;
    
    [JsonInclude]
    public string? OriginalTitle { get; set; }
    
    [JsonInclude]
    public string? OriginalArtist { get; set; }
    public override string GetDownloadUrl()
    {
        return ID;
    }

    public override string SerializeSelf()
    {
        return JsonSerializer.Serialize(this);
    }
}