using AudioManager.Platforms.Errors;
using AudioManager.Platforms.Optional;
using Result;
using Result.Objects;

namespace AudioManager.Platforms;

public abstract class Platform : ISupportsID
{
    public abstract HashSet<string> SearchIDIdentifiers { get; }
    public abstract HashSet<string> PlatformDomains { get; }
    public abstract HashSet<string> SearchPlaylistIdentifiers { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract int Priority { get; }
    
    protected abstract List<SearchProvider> SearchProviders { get; set; }
    protected abstract List<ContentGetter> ContentDownloaders { get; set; }

    public virtual void Initialize()
    {
        SortProviders();
        SetupSearchProviders();
    }

    protected virtual void SortProviders()
    {
        SearchProviders = SearchProviders.OrderByDescending(x => x.Priority).ToList();
        ContentDownloaders = ContentDownloaders.OrderByDescending(x => x.Priority).ToList();
    }

    protected virtual void SetupSearchProviders()
    {
        SearchProviders.ForEach(s => s.RegisterContentDownloaders(ContentDownloaders));
    }
    
    public virtual async Task<Result<PlatformResult, SearchError>> TryID(string id,
        CancellationToken cancellation_token = default)
    {
        foreach (var search_provider in 
                 SearchProviders.Where(search_provider => search_provider is ISupportsID)
                     .Cast<ISupportsID>())
        {
            var result = await search_provider.TryID(id, cancellation_token);
            if (result == Status.OK) return result;
        }

        return Result<PlatformResult, SearchError>.Error(default);
    }
}