using AudioManagement.Platforms.Errors;
using AudioManagement.Platforms.Optional.Supports;
using AudioManagement.Platforms.YouTube.Cache;
using Result;
using Result.Objects;

namespace AudioManagement.Platforms.YouTube.Search_Providers;

public class YouTubeSearchProvider_Cached(YouTubeCacher cacher) : SearchProvider, ISupportsID
{
    protected readonly YouTubeCacher YouTubeCacher = cacher;
    public override string Name => "YouTube Cached Results";
    public override string PlatformIdentifier => "yt://";
    public override int Priority => 99;

    public async Task<Result<PlatformResult, SearchError>> TryID(string id,
        CancellationToken cancellation_token = default)
    {
        var result = await YouTubeCacher.GetFromCacheAsync(id);
        if (result == Status.Error) return Result<PlatformResult, SearchError>.Error(SearchError.NotFound);

        var ok_result = result.GetOK();
        ok_result.Downloaders = ContentDownloaders;
        return Result<PlatformResult, SearchError>.Success(ok_result);
    }

    protected override void Initialize()
    {
        YouTubeCacher.InitializeAsync().GetAwaiter().GetResult();
    }
}