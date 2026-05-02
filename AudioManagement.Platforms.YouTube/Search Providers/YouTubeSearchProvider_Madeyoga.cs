using AudioManagement.Platforms.Errors;
using AudioManagement.Platforms.Optional.Supports;
using AudioManagement.Utils;
using Result;
using Serilog;
using YoutubeSearchApi.Net;
using YoutubeSearchApi.Net.Backends;
using YoutubeSearchApi.Net.Objects;

namespace AudioManagement.Platforms.YouTube.Search_Providers;

public class YouTubeSearchProviderMadeyoga(ILogger logger) : SearchProvider(logger),
    ISupportsSearch
{
    public override string Name => "YouTubeSearchAPI.Net";
    public override string PlatformIdentifier => "yt://";
    public override int Priority => 20;
    protected DefaultSearchClient Client { get; } = new(new YoutubeSearchBackend());

    public async Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchKeywords(string keywords,
        CancellationToken cancellationToken = default)
    {
        var search = await Client.SearchAsync(HttpClientManager.GetHttpClient(), keywords, 15);
        if (search == null) return Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.NotFound);

        return Result<IEnumerable<PlatformResult>, SearchError>.Success(
            search.Results.Where(r => r is YoutubeVideo v && !string.IsNullOrWhiteSpace(v.Duration))
                .Cast<YoutubeVideo>()
                .Select(v => new YouTubeResult
                {
                    ID = "yt://" + v.Id,
                    Downloaders = ContentDownloaders,
                    Name = v.Title,
                    Artist = v.Author,
                    Duration = TimeSpan.ParseExact(v.Duration, [@"h\:m\:s", @"m\:s", "s"], null),
                    ThumbnailUrl = v.ThumbnailUrl.SliceTo("?") // remove tracking data
                }));
    }
}