using AudioManager.Platforms.Errors;
using AudioManager.Platforms.Local.Manager;
using AudioManager.Platforms.Optional.Supports;
using Result;
using Result.Objects;

namespace AudioManager.Platforms.Local.Search_Providers;

public class MusicSearchProvider : SearchProvider, ISupportsID, ISupportsSearch
{
    protected readonly MusicManager MusicManager = new();
    public override string Name => "Music Search";
    public override string PlatformIdentifier => "audio://";
    public override int Priority => 99;

    protected override void Initialize()
    {
        MusicManager.Initialize();
        base.Initialize();
    }

    public Task<Result<PlatformResult, SearchError>> TryID(string id, CancellationToken cancellation_token = default)
    {
        var found = MusicManager.SearchById(id);
        if (found == Status.Error) return Task.FromResult(Result<PlatformResult, SearchError>.Error(SearchError.NotFound));
        
        var result = found.GetOK();
        return Task.FromResult(Result<PlatformResult, SearchError>
            .Success(result.ToMusicResult(ContentDownloaders)));
    }
    
    public Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchKeywords(string keywords, CancellationToken cancellation_token = default)
    {
        var search = MusicManager.SearchOneByTerm(keywords);
        if (search == Status.Error) return Task.FromResult(Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.NotFound));
        
        var result = search.GetOK();
        return Task.FromResult(Result<IEnumerable<PlatformResult>, SearchError>.Success([
            result.ToMusicResult(ContentDownloaders)
        ]));
    }
}