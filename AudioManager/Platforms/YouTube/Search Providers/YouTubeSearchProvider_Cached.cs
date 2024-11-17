using AudioManager.Platforms.Errors;
using AudioManager.Platforms.Optional.Supports;
using AudioManager.Platforms.YouTube.Cache;
using Result;
using Result.Objects;

namespace AudioManager.Platforms.YouTube.Search_Providers;

public class YouTubeSearchProvider_Cached(YouTubeCacher cacher) : SearchProvider, ISupportsID
{
    public override string Name => "YouTube Cached Results";
    public override string PlatformIdentifier => "yt://";
    public override int Priority => 99;

    protected readonly YouTubeCacher YouTubeCacher = cacher;

    protected override void Initialize()
    {
        YouTubeCacher.InitializeAsync().GetAwaiter().GetResult();
    }

    public async Task<Result<PlatformResult, SearchError>> TryID(string id, CancellationToken cancellation_token = default)
    {
        var result = await YouTubeCacher.GetFromCacheAsync(id);
        return result != Status.Error ? 
            Result<PlatformResult, SearchError>.Success(result.GetOK()) :
            Result<PlatformResult, SearchError>.Error(SearchError.NotFound);
    }
}