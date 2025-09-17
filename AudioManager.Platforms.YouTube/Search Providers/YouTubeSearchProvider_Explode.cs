using Audio.Utils;
using AudioManager.Platforms.Errors;
using AudioManager.Platforms.Optional.Supports;
using Result;
using YoutubeExplode;
using YoutubeExplode.Common;

namespace AudioManager.Platforms.YouTube.Search_Providers;

public sealed class YouTubeSearchProvider_Explode : SearchProvider,
    ISupportsID, ISupportsPlaylist, ISupportsSearch
{
    public static YoutubeClient Client { get; } = new();
    public override string Name => "YouTube Explode";
    public override string PlatformIdentifier => "yt://";
    public override int Priority => 40;

    private static string RemoveTracking(string thumbnailUrl)
    {
        return thumbnailUrl.SliceTo("?");
    }

    public async Task<Result<PlatformResult, SearchError>> TryID(string id, CancellationToken token)
    {
        try
        {
            var youtube_client = Client;
            var video = await youtube_client.Videos.GetAsync(id, token);


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

    public async Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchKeywords(string keywords,
        CancellationToken token)
    {
        try
        {
            var youtube_client = new YoutubeClient();
            var results = await youtube_client.Search.GetVideosAsync(keywords, token).CollectAsync(15);
            return Result<IEnumerable<PlatformResult>, SearchError>.Success(
                results.Select(video => new YouTubeResult
                {
                    ID = PlatformIdentifier + video.Id,
                    Name = video.Title,
                    Artist = video.Author.ChannelTitle,
                    Duration = video.Duration.GetValueOrDefault(TimeSpan.Zero),
                    ThumbnailUrl = RemoveTracking(video.Thumbnails.OrderByDescending(t => t.Resolution.Area).First().Url),
                    Downloaders = ContentDownloaders
                }));
        }
        catch
        {
            return Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.GenericError);
        }
    }

    public async Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchPlaylist(string playlist_url, CancellationToken cancellation_token)
    {
        try
        {
            var youtube_client = new YoutubeClient();
            var playlist_id = playlist_url
                .SliceAfter("list=")
                .SliceTo("&");

            var playlist_results = new List<PlatformResult>();
            await foreach (var video in youtube_client.Playlists.GetVideoBatchesAsync(playlist_id, cancellation_token))
            {
                var items = video.Items;
                playlist_results.AddRange(items.Select(v => new YouTubeResult
                {
                    ID = PlatformIdentifier + v.Id,
                    Name = v.Title,
                    Artist = v.Author.ChannelTitle,
                    Duration = v.Duration.GetValueOrDefault(TimeSpan.Zero),
                    Downloaders = ContentDownloaders,
                    ThumbnailUrl = RemoveTracking(v.Thumbnails.OrderByDescending(t => t.Resolution.Area).First().Url)
                }));
            }

            return Result<IEnumerable<PlatformResult>, SearchError>.Success(playlist_results);
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
}