using AudioManager.Platforms.Errors;
using Result;

namespace AudioManager.Platforms.YouTube;

public class YouTubeSearchProvider_Explode : SearchProvider
{
    public override string Name => "YouTube Explode";
    public override int Priority => 0;
    
    public override bool SupportsID => true;
    public override bool SupportsSearch => true;

    public override async Task<Result<PlatformResult, SearchError>> TryID(string id, CancellationToken token)
    {
        return Result<PlatformResult, SearchError>.Success(new YouTubeResult
        {
            ID = "",
            Downloaders = []
        });
    }

    public override async Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchResults(string keywords, CancellationToken token)
    {
        throw new NotImplementedException();
    }
    
    public override async Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchResultsPaginated(string keywords, int page, int page_size, 
        CancellationToken token)
    {
        throw new NotImplementedException();
    }
}