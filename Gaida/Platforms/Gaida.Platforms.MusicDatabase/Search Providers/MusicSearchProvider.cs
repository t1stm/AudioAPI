using Gaida.Core.Platforms.Errors;
using Gaida.Platforms.MusicDatabase.Manager;
using Gaida.Core.Platforms.Optional.Supports;
using Result;
using Result.Objects;
using Serilog;
using Gaida.Core.Platforms;

namespace Gaida.Platforms.MusicDatabase.Search_Providers;

public class MusicSearchProvider(ILogger logger) : SearchProvider(logger), ISupportsID, ISupportsSearch, ISupportsRandomResults
{
    protected readonly MusicManager MusicManager = new(logger);
    public override string Name => "Music Search";
    public override string PlatformIdentifier => "audio://";
    public override int Priority => 99;

    public Task<Result<PlatformResult, SearchError>> TryID(string id, CancellationToken cancellationToken = default)
    {
        Logger.Debug("MusicSearchProvider: Searching for ID: {Id}", id);
        var found = MusicManager.SearchById(id);
        if (found == Status.Error)
        {
            Logger.Information("MusicSearchProvider: ID not found: {Id}", id);
            return Task.FromResult(Result<PlatformResult, SearchError>.Error(SearchError.NotFound));
        }

        var result = found.GetOk();
        Logger.Debug("MusicSearchProvider: Found result for ID: {Id} - {Title}", id, result.OriginalTitle);
        return Task.FromResult(Result<PlatformResult, SearchError>
            .Success(result.ToMusicResult(ContentDownloaders)));
    }

    public Task<Result<IEnumerable<PlatformResult>, SearchError>> GetRandomResults(int count)
    {
        Logger.Debug("MusicSearchProvider: Getting {Count} random results", count);
        var results = MusicManager.GetRandomSongs(count);
        if (results == Status.Error)
            return Task.FromResult(Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.NotFound));

        var ok = results.GetOk().ToList();
        Logger.Debug("MusicSearchProvider: Found {Count} random results", ok.Count);
        return Task.FromResult(
            Result<IEnumerable<PlatformResult>, SearchError>.Success(ok.Select(song =>
                song.ToMusicResult(ContentDownloaders))));
    }

    public Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchKeywords(string keywords,
        CancellationToken cancellationToken = default)
    {
        Logger.Debug("MusicSearchProvider: Searching for keywords: {Keywords}", keywords);
        var search = MusicManager.SearchByTerm(keywords);
        if (search == Status.Error)
        {
            Logger.Information("MusicSearchProvider: No results found for keywords: {Keywords}", keywords);
            return Task.FromResult(Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.NotFound));
        }

        var results = search.GetOk().ToList();
        Logger.Debug("MusicSearchProvider: Found {Count} results for keywords: {Keywords}", results.Count, keywords);
        return Task.FromResult(
            Result<IEnumerable<PlatformResult>, SearchError>.Success(results.Select(r =>
                r.ToMusicResult(ContentDownloaders))));
    }

    protected override void Initialize()
    {
        Logger.Debug("Initializing MusicSearchProvider");
        _ = MusicManager.Initialize();
        base.Initialize();
    }

    public Task<Result<IEnumerable<PlatformResult>, SearchError>> GetArtistSongs(string artist)
    {
        Logger.Debug("MusicSearchProvider: Getting songs for artist: {Artist}", artist);
        var results = MusicManager.GetArtistSongs(artist);
        if (results == Status.Error)
        {
            Logger.Information("MusicSearchProvider: No songs found for artist: {Artist}", artist);
            return Task.FromResult(Result<IEnumerable<PlatformResult>, SearchError>.Error(SearchError.NotFound));
        }

        var ok = results.GetOk().ToList();
        Logger.Debug("MusicSearchProvider: Found {Count} songs for artist: {Artist}", ok.Count, artist);
        return Task.FromResult(
            Result<IEnumerable<PlatformResult>, SearchError>.Success(ok.Select(song =>
                song.ToMusicResult(ContentDownloaders))));
    }
}