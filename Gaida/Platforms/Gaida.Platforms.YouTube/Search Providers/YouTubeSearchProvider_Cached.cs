using Gaida.Core.Platforms.Errors;
using Gaida.Core.Platforms.Optional.Supports;
using Gaida.Platforms.YouTube.Cache;
using Result;
using Result.Objects;
using Serilog;
using Gaida.Core.Platforms;

namespace Gaida.Platforms.YouTube.Search_Providers;

public class YouTubeSearchProviderCached(ILogger logger, YouTubeCacher cacher) : SearchProvider(logger), ISupportsID
{
    protected readonly YouTubeCacher YouTubeCacher = cacher;
    public override string Name => "YouTube Cached Results";
    public override string PlatformIdentifier => "yt://";
    public override int Priority => 99;

    public async Task<Result<PlatformResult, SearchError>> TryID(string id,
        CancellationToken cancellationToken = default)
    {
        var result = await YouTubeCacher.GetFromCacheAsync(id);
        Logger.Debug("YouTube Cached Result: {Result}", result);
        if (result == Status.Error)
        {
            Logger.Error("Failed to get YouTube cached result for ID: {ID}, Error: {@Error}", id, result);
            return Result<PlatformResult, SearchError>.Error(SearchError.NotFound);
        }
        

        var okResult = result.GetOk();
        okResult.Downloaders = ContentDownloaders;
        return Result<PlatformResult, SearchError>.Success(okResult);
    }

    protected override void Initialize()
    {
        YouTubeCacher.InitializeAsync().GetAwaiter().GetResult();
    }
}