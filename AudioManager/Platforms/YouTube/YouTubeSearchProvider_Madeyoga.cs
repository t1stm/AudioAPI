using System.Globalization;
using Audio;
using AudioManager.Platforms.Errors;
using AudioManager.Platforms.Optional;
using Result;
using YoutubeSearchApi.Net;
using YoutubeSearchApi.Net.Backends;
using YoutubeSearchApi.Net.Objects;

namespace AudioManager.Platforms.YouTube;

public class YouTubeSearchProvider_Madeyoga : SearchProvider, 
    ISupportsSearch
{
    public override string Name => "YouTubeSearchAPI.Net";
    public override string PlatformIdentifier => "yt://";
    public override int Priority => 20;
    protected DefaultSearchClient Client { get; } = new(new YoutubeSearchBackend());

    public async Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchKeywords(string keywords, CancellationToken cancellation_token = default)
    {
        var search = await Client.SearchAsync(HttpClientManager.GetHttpClient(), keywords, 15);
        if (search == null) return Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.NotFound);
        
        return Result<IEnumerable<PlatformResult>, SearchError>.Success(
            search.Results.Where(r => r is YoutubeVideo)
                .Cast<YoutubeVideo>()
                .Select(v => new YouTubeResult
                {
                    ID = "yt://" + v.Id,
                    Downloaders = ContentDownloaders,
                    Name = v.Title,
                    Artist = v.Author,
                    Duration = TimeSpan.ParseExact(v.Duration, [@"h\:m\:s", @"m\:s", "s"], null),
                    ThumbnailUrl = v.ThumbnailUrl.Split('?')[0] // remove tracking data
                }));
    }
}