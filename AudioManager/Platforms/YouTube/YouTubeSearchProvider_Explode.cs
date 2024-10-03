using AudioManager.Platforms.Errors;
using Result;
using YoutubeExplode;
using YoutubeExplode.Search;

namespace AudioManager.Platforms.YouTube;

public sealed class YouTubeSearchProvider_Explode : SearchProvider
{
    public override string Name => "YouTube Explode";
    public override int Priority => 0;
    
    public override bool SupportsID => true;
    public override bool SupportsSearch => true;
    public override bool SupportsPlaylists => true;
    public override bool SupportsPagination => false;

    public override async Task<Result<PlatformResult, SearchError>> TryID(string id, CancellationToken token)
    {
        try
        {
            var youtube_client = new YoutubeClient();
            var video = await youtube_client.Videos.GetAsync(id, token);
        
            return Result<PlatformResult, SearchError>.Success(new YouTubeResult
            {
                Name = video.Title,
                Artist = video.Author.ChannelTitle,
                Duration = video.Duration.GetValueOrDefault(TimeSpan.Zero),
                ID = id,
                Downloaders = ContentDownloaders
            });
        }
        catch
        {
            return Result<PlatformResult, SearchError>.Error(SearchError.GenericError);
        }
    }

    public override async Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchResults(string keywords, CancellationToken token)
    {
        try
        {
            var youtube_client = new YoutubeClient();
            var search_results = new List<PlatformResult>();
            
            await foreach (var result in youtube_client.Search.GetResultsAsync(keywords, token))
            {
                if (result is not VideoSearchResult video) continue;
                
                search_results.Add(new YouTubeResult
                {
                    ID = video.Id,
                    Name = video.Title,
                    Artist = video.Author.ChannelTitle,
                    Duration = video.Duration.GetValueOrDefault(TimeSpan.Zero),
                    ThumbnailUrl = video.Thumbnails[0].Url, 
                    Downloaders = ContentDownloaders
                });
            }

            return Result<IEnumerable<PlatformResult>, SearchError>.Success(search_results);
        }
        catch
        {
            return Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.GenericError);
        }
    }
    
    public override Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchResultsPaginated(string keywords, int page, int page_size, 
        CancellationToken token)
    {
        return Task.FromResult(Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.NotSupported));
    }

    public override async Task<Result<IEnumerable<PlatformResult>, SearchError>> TryPlaylist(string playlist_url, CancellationToken cancellation_token)
    {
        try
        {
            var youtube_client = new YoutubeClient();
            var playlist_id = playlist_url
                .Split("list=").Last()
                .Split("&").First();

            var playlist_results = new List<PlatformResult>();
            await foreach (var video in youtube_client.Playlists.GetVideoBatchesAsync(playlist_id, cancellation_token))
            {
                var items = video.Items;
                playlist_results.AddRange(items.Select(v => new YouTubeResult
                {
                    ID = v.Id,
                    Name = v.Title,
                    Artist = v.Author.ChannelTitle,
                    Duration = v.Duration.GetValueOrDefault(TimeSpan.Zero),
                    Downloaders = ContentDownloaders,
                    ThumbnailUrl = v.Thumbnails[0].Url
                }));
            }

            return Result<IEnumerable<PlatformResult>, SearchError>.Success(playlist_results);
        }
        catch
        {
            return Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.GenericError);
        }
    }
}