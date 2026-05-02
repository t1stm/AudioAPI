using Serilog;

namespace AudioManagement.Platforms;

public abstract class SearchProvider(ILogger logger)
{
    public ILogger Logger { get; } = logger;
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