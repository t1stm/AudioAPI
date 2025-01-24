using System.Text.Json.Serialization;
using AudioManager.Platforms.Optional.Supports;
using AudioManager.Streams;

namespace AudioManager.Platforms.YouTube;

public sealed class YouTubeResult : PlatformResult, ISupportsCaching
{
    [JsonIgnore]
    public string PureId => ID.Split("://")[1];
    
    public override string GetDownloadUrl()
    {
        return $"https://www.youtube.com/watch?v={PureId}";
    }

    public async Task RunCacheProcess(StreamSpreader stream_spreader)
    {
        await YouTubeCacheProvider.UpdateCache(this, stream_spreader);
    }
}