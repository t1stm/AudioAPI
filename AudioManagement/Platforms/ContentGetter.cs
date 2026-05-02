using AudioManagement.Platforms.Errors;
using AudioManagement.Streams;
using Result;

namespace AudioManagement.Platforms;

public abstract class ContentGetter
{
    public abstract Task<Result<StreamSpreader, DownloadError>> TryGetContentData(PlatformResult result, CancellationToken cancellation_token);
    public abstract int Priority { get; }
    public virtual void Initialize() { }
}