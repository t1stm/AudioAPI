using Gaida.Core.Streams;

namespace Gaida.Core.Platforms.Optional.Supports;

public interface ISupportsCaching
{
    public Task RunCacheProcess(StreamSpreader streamSpreader);
}