using Serilog;

namespace Gaida.Core.Platforms;

public abstract class SearchProvider
{
    protected SearchProvider(ILogger logger)
    {
        Logger = logger.ForContext(GetType());
    }

    protected ILogger Logger { get; }

    public abstract string PlatformIdentifier { get; }
    public abstract int Priority { get; }
    protected virtual List<ContentGetter> ContentDownloaders { get; set; } = [];

    public virtual void RegisterContentDownloaders(List<ContentGetter> contentDownloaders)
    {
        ContentDownloaders = contentDownloaders;
        Initialize();
    }

    protected virtual void Initialize()
    {
        ContentDownloaders.ForEach(downloader => downloader.Initialize());
    }
}