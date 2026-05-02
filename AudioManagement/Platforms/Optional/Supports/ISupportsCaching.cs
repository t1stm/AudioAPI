using AudioManagement.Streams;

namespace AudioManagement.Platforms.Optional.Supports;

public interface ISupportsCaching
{
    public Task RunCacheProcess(StreamSpreader stream_spreader);
}