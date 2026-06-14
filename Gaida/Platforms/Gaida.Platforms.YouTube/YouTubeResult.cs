using System.Text.Json.Serialization;
using Gaida.Core.Platforms.Optional.Supports;
using Gaida.Core.Streams;
using Gaida.Core.Platforms;

namespace Gaida.Platforms.YouTube;

public sealed class YouTubeResult : PlatformResult, ISupportsCaching
{
    [JsonIgnore]
    public ReadOnlySpan<char> PureId
    {
        get
        {
            var span = ID.AsSpan();
            Span<Range> ranges = stackalloc Range[2];

            var count = span.Split(ranges, "://");
            return count > 1 ? span[ranges[1]] : span;
        }
    }

    public async Task RunCacheProcess(StreamSpreader streamSpreader)
    {
        await YouTubeCacheProvider.UpdateCache(this, streamSpreader);
    }

    public override string GetDownloadUrl()
    {
        return $"https://www.youtube.com/watch?v={PureId}";
    }
}