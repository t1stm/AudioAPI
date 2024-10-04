namespace AudioManager.Platforms;

public abstract class SearchProvider
{
    public abstract string Name { get; }
    public abstract string PlatformIdentifier { get; }
    public abstract int Priority { get; }
    protected virtual List<ContentDownloader> ContentDownloaders { get; set; } = [];

    public virtual void RegisterContentDownloaders(List<ContentDownloader> content_downloaders)
    {
        ContentDownloaders = content_downloaders;
    }

    protected virtual void Initialize() { }
}