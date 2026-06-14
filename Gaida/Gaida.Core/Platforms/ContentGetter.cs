using Gaida.Core.Platforms.Errors;
using Gaida.Core.Streams;
using Result;
using Serilog;

namespace Gaida.Core.Platforms;

public abstract class ContentGetter
{
    public abstract int Priority { get; }
    protected ILogger Logger { get; }

    protected ContentGetter(ILogger logger)
    {
        Logger = logger.ForContext(GetType());
    }

    public abstract Task<Result<StreamSpreader, DownloadError>> TryGetContentData(PlatformResult result,
        CancellationToken cancellationToken);

    public virtual void Initialize()
    {
    }
}