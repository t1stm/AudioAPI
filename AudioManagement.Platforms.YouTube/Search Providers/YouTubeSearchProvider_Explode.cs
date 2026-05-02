using AudioManagement.Platforms.Errors;
using AudioManagement.Platforms.Optional.Supports;
using AudioManagement.Utils;
using Result;
using YoutubeExplode;
using YoutubeExplode.Common;

namespace AudioManagement.Platforms.YouTube.Search_Providers;

public sealed class YouTubeSearchProviderExplode : SearchProvider,
    ISupportsID, ISupportsPlaylist, ISupportsSearch
{
    public static YoutubeClient Client { get; } = new();
    public override string Name => "YouTube Explode";
    public override string PlatformIdentifier => "yt://";
    public override int Priority => 40;

    public async Task<Result<PlatformResult, SearchError>> TryID(string id, CancellationToken token)
    {
        try
        {
            var youtubeClient = Client;
            var video = await youtubeClient.Videos.GetAsync(id, token);


            return Result<PlatformResult, SearchError>.Success(new YouTubeResult
            {
                Name = video.Title,
                Artist = video.Author.ChannelTitle,
                Duration = video.Duration.GetValueOrDefault(TimeSpan.Zero),
                ID = PlatformIdentifier + id,
                ThumbnailUrl = RemoveTracking(video.Thumbnails.OrderByDescending(t => t.Resolution.Area).First().Url),
                Downloaders = ContentDownloaders
            });
        }
        catch
        {
            return Result<PlatformResult, SearchError>.Error(SearchError.GenericError);
        }
    }

    public async Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchPlaylist(string playlistUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var youtubeClient = new YoutubeClient();
            var playlistID = playlistUrl
                .SliceAfter("list=")
                .SliceTo("&");

            var playlistResults = new List<PlatformResult>();
            await foreach (var video in youtubeClient.Playlists.GetVideoBatchesAsync(playlistID, cancellationToken))
            {
                var items = video.Items;
                playlistResults.AddRange(items.Select(v => new YouTubeResult
                {
                    ID = PlatformIdentifier + v.Id,
                    Name = v.Title,
                    Artist = v.Author.ChannelTitle,
                    Duration = v.Duration.GetValueOrDefault(TimeSpan.Zero),
                    Downloaders = ContentDownloaders,
                    ThumbnailUrl = RemoveTracking(v.Thumbnails.OrderByDescending(t => t.Resolution.Area).First().Url)
                }));
            }

            return Result<IEnumerable<PlatformResult>, SearchError>.Success(playlistResults);
        }
        catch
        {
            return Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.GenericError);
        }
    }

    public bool IsPlaylistUrl(ReadOnlySpan<char> query)
    {
        throw new NotSupportedException();
    }

    public async Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchKeywords(string keywords,
        CancellationToken token)
    {
        try
        {
            var youtubeClient = new YoutubeClient();
            var results = await youtubeClient.Search.GetVideosAsync(keywords, token).CollectAsync(15);
            return Result<IEnumerable<PlatformResult>, SearchError>.Success(
                results.Select(video => new YouTubeResult
                {
                    ID = PlatformIdentifier + video.Id,
                    Name = video.Title,
                    Artist = video.Author.ChannelTitle,
                    Duration = video.Duration.GetValueOrDefault(TimeSpan.Zero),
                    ThumbnailUrl =
                        RemoveTracking(video.Thumbnails.OrderByDescending(t => t.Resolution.Area).First().Url),
                    Downloaders = ContentDownloaders
                }));
        }
        catch
        {
            return Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.GenericError);
        }
    }

    private static string RemoveTracking(string thumbnailUrl)
    {
        return thumbnailUrl.SliceTo("?");
    }
}