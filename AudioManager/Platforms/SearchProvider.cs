namespace AudioManager.Platforms;

public abstract class SearchProvider
{
    public abstract string Name { get; }
    public abstract string PlatformIdentifier { get; }
    public abstract int Priority { get; }
    protected virtual List<ContentGetter> ContentDownloaders { get; set; } = [];

    public virtual void RegisterContentDownloaders(List<ContentGetter> content_downloaders)
    {
        ContentDownloaders = content_downloaders;
        Initialize();
    }

    protected virtual void Initialize()
    {
        ContentDownloaders.ForEach(downloader => downloader.Initialize());
    }
}