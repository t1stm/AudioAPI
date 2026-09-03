using Gaida.Core.Streams;
using Serilog;

namespace Gaida.Core.Platforms;

public abstract class ContentGetter
{
    protected ContentGetter(ILogger logger)
    {
        Logger = logger.ForContext(GetType());
    }

    public abstract int Priority { get; }
    protected ILogger Logger { get; }

    /// <returns>The content stream, or <c>null</c> when this getter can't serve the result.</returns>
    public abstract Task<StreamSpreader?> GetContentDataAsync(PlatformResult result,
        CancellationToken cancellationToken);

    public virtual void Initialize()
    {
    }
}