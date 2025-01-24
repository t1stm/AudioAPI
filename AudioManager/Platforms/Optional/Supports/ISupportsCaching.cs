using AudioManager.Streams;

namespace AudioManager.Platforms.Optional.Supports;

public interface ISupportsCaching
{
    public Task RunCacheProcess(StreamSpreader stream_spreader);
}