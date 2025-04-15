using AudioManager.Platforms.Errors;
using AudioManager.Platforms.MusicDatabase.Manager;
using AudioManager.Platforms.Optional.Supports;
using Result;
using Result.Objects;

namespace AudioManager.Platforms.MusicDatabase.Search_Providers;

public class MusicSearchProvider : SearchProvider, ISupportsID, ISupportsSearch, ISupportsRandomResults
{
    protected readonly MusicManager MusicManager = new();
    public override string Name => "Music Search";
    public override string PlatformIdentifier => "audio://";
    public override int Priority => 99;

    protected override void Initialize()
    {
        _ = MusicManager.Initialize();
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
        var search = MusicManager.SearchByTerm(keywords);
        if (search == Status.Error) return Task.FromResult(Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.NotFound));

        var results = search.GetOK();
        return Task.FromResult(Result<IEnumerable<PlatformResult>, SearchError>.Success(results.Select(r => r.ToMusicResult(ContentDownloaders))));
    }

    public Task<Result<IEnumerable<PlatformResult>, SearchError>> GetRandomResults(int count)
    {
        var results = MusicManager.GetRandomSongs(count);
        if (results == Status.Error) return Task.FromResult(Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.NotFound));

        var ok = results.GetOK();
        return Task.FromResult(Result<IEnumerable<PlatformResult>, SearchError>.Success(ok.Select(song => song.ToMusicResult(ContentDownloaders))));
    }

    public Task<Result<IEnumerable<PlatformResult>, SearchError>> GetArtistSongs(string artist)
    {
        var results = MusicManager.GetArtistSongs(artist);
        if (results == Status.Error) return Task.FromResult(Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.NotFound));

        var ok = results.GetOK();
        return Task.FromResult(Result<IEnumerable<PlatformResult>, SearchError>.Success(ok.Select(song => song.ToMusicResult(ContentDownloaders))));
    }
}