using System.Text.Json.Serialization;
using AudioManager.Platforms.Optional.Supports;
using AudioManager.Streams;

namespace AudioManager.Platforms.YouTube;

public sealed class YouTubeResult : PlatformResult, ISupportsCaching
{
    [JsonIgnore]
    public ReadOnlySpan<char> PureId {
        get
        {
            var span = ID.AsSpan();
            Span<Range> ranges = stackalloc Range[2];
            
            var count = span.Split(ranges, "://");
            return count > 1 ? span[ranges[1]] : span;
        } 
    }

    public override string GetDownloadUrl()
    {
        return $"https://www.youtube.com/watch?v={PureId}";
    }

    public async Task RunCacheProcess(StreamSpreader stream_spreader)
    {
        await YouTubeCacheProvider.UpdateCache(this, stream_spreader);
    }
}