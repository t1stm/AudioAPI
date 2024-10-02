using AudioManager.Platforms.Errors;
using Result;
using Result.Objects;

namespace AudioManager.Platforms;

public abstract class Platform
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract int Priority { get; }
    public abstract bool SupportsID { get; }
    public abstract bool SupportsSearch { get; }
    protected abstract List<SearchProvider> SearchProviders { get; set; }
    protected abstract List<ContentDownloader> ContentDownloaders { get; set; }

    public virtual async Task<Result<PlatformResult, SearchError>> SearchID(string id,
        CancellationToken cancellation_token = default)
    {
        foreach (var search_provider in SearchProviders.Where(search_provider => search_provider.SupportsID))
        {
            var result = await search_provider.TryID(id, cancellation_token);
            if (result == Status.OK) return result;
        }

        return Result<PlatformResult, SearchError>.Error(default);
    }

    public virtual async Task<Result<IEnumerable<PlatformResult>, SearchError>> SearchKeywords(string keywords,
        CancellationToken cancellation_token = default)
    {
        foreach (var search_provider in SearchProviders.Where(search_provider => search_provider.SupportsSearch))
        {
            var result = await search_provider.TrySearchResults(keywords, cancellation_token);
            if (result == Status.OK) return result;
        }

        return Result<IEnumerable<PlatformResult>, SearchError>.Error(default);
    }
    
    public virtual async Task<Result<IEnumerable<PlatformResult>, SearchError>> SearchKeywordsPaginated(string keywords, int page = 1, int page_size = 10, 
        CancellationToken cancellation_token = default)
    {
        foreach (var search_provider in SearchProviders.Where(search_provider => search_provider.SupportsSearch))
        {
            var result = await search_provider.TrySearchResultsPaginated(keywords, page, page_size, cancellation_token);
            if (result == Status.OK) return result;
        }

        return Result<IEnumerable<PlatformResult>, SearchError>.Error(default);
    }
    
    public virtual void Initialize() { }

    public virtual void SortSearchProviders()
    {
        SearchProviders = SearchProviders.OrderByDescending(x => x.Priority).ToList();
    }
}