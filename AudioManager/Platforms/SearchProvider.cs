using AudioManager.Platforms.Errors;
using Result;

namespace AudioManager.Platforms;

public abstract class SearchProvider
{
    public abstract string Name { get; }
    public abstract int Priority { get; }
    
    public abstract bool SupportsID { get; }
    public abstract bool SupportsSearch { get; }
    
    public abstract Task<Result<PlatformResult, SearchError>> TryID(string id, CancellationToken token);
    public abstract Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchResults(string keywords, CancellationToken token);
    public abstract Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchResultsPaginated(string keywords, int page, int page_size, 
        CancellationToken token);
    
    protected virtual void Initialize() { }
}