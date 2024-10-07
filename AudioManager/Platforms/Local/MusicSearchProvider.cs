using AudioManager.Platforms.Errors;
using AudioManager.Platforms.Optional;
using Result;

namespace AudioManager.Platforms.Local;

public class MusicSearchProvider : SearchProvider, ISupportsID, ISupportsSearch
{
    public override string Name => "Music Search";
    public override string PlatformIdentifier => "audio://";
    public override int Priority => 99;
    
    public async Task<Result<PlatformResult, SearchError>> TryID(string id, CancellationToken cancellation_token = default)
    {
        return Result<PlatformResult, SearchError>.Success(new MusicResult
        {
            ID = id,
            Downloaders = ContentDownloaders,
            Name = "Dummy Result",
            Artist = "Dummy Artist",
            Album = "Dummy Album",
            ThumbnailUrl = "Dummy Thumbnail.jpg"
        });
    }
    public async Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchKeywords(string keywords, CancellationToken cancellation_token = default)
    {
        return Result<IEnumerable<PlatformResult>, SearchError>.Success([
            new MusicResult
            {
                ID = "audio://dummy",
                Downloaders = ContentDownloaders,
                Name = "Dummy Result",
                Artist = "Dummy Artist",
                Album = "Dummy Album",
                ThumbnailUrl = "Dummy Thumbnail.jpg"
            }]);
    }
}