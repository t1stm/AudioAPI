using Serilog;

namespace Gaida.Core.Platforms;

public abstract class SearchProvider
{
    protected ILogger Logger { get; }

    protected SearchProvider(ILogger logger)
    {
        Logger = logger.ForContext(GetType());
    }
    
    public abstract string Name { get; }
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