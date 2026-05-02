using AudioManagement.Platforms.Errors;
using AudioManagement.Streams;
using Result;
using Serilog;

namespace AudioManagement.Platforms;

public abstract class ContentGetter(ILogger logger)
{
    public abstract int Priority { get; }
    protected ILogger Logger { get; } = logger.ForContext<ContentGetter>();

    public abstract Task<Result<StreamSpreader, DownloadError>> TryGetContentData(PlatformResult result,
        CancellationToken cancellationToken);

    public virtual void Initialize()
    {
    }
}