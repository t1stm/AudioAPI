using Gaida.Core.Platforms.Errors;
using Gaida.Platforms.MusicDatabase.Getters;
using Gaida.Platforms.MusicDatabase.Search_Providers;
using Gaida.Core.Platforms.Optional.Supports;
using Result;
using Serilog;
using Gaida.Core.Platforms;

namespace Gaida.Platforms.MusicDatabase;

public class MusicDatabase(ILogger logger) : Platform(logger), IPlatformFactory<MusicDatabase>, ISupportsSearch, ISupportsRandomResults
{
    protected override HashSet<string> SearchIDIdentifiers { get; } = ["audio://"];
    protected override HashSet<string> PlatformDomains { get; } = [];
    protected override HashSet<string> SearchPlaylistIdentifiers { get; } = [];

    public override string Name => "Music Database";
    public override string Description => "Locally stored music";
    public override int Priority => 99;

    public static MusicDatabase CreateNew(ILogger logger)
    {
        return new MusicDatabase(logger);
    }
    
    protected override List<SearchProvider> SearchProviders { get; set; } =
    [
        new MusicSearchProvider(logger)
    ];

    protected override List<ContentGetter> ContentDownloaders { get; set; } =
    [
        new MusicGetter(logger)
    ];

    public Task<Result<IEnumerable<PlatformResult>, SearchError>> GetRandomResults(int count)
    {
        Logger.Debug("Getting {Count} random results from MusicDatabase", count);
        var provider = (MusicSearchProvider)SearchProviders[0];
        return provider.GetRandomResults(count);
    }

    public Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchKeywords(string keywords,
        CancellationToken cancellationToken = default)
    {
        Logger.Debug("Searching for keywords in MusicDatabase: {Keywords}", keywords);
        var provider = (MusicSearchProvider)SearchProviders[0];
        return provider.TrySearchKeywords(keywords, cancellationToken);
    }

    public override Task<Result<PlatformResult, SearchError>> TryID(string id,
        CancellationToken cancellationToken = default)
    {
        Logger.Debug("Searching for ID in MusicDatabase: {Id}", id);
        var provider = (MusicSearchProvider)SearchProviders[0];
        return provider.TryID(id, cancellationToken);
    }

    public Task<Result<IEnumerable<PlatformResult>, SearchError>> GetArtistSongs(string artist)
    {
        Logger.Debug("Getting songs for artist in MusicDatabase: {Artist}", artist);
        var provider = (MusicSearchProvider)SearchProviders[0];
        return provider.GetArtistSongs(artist);
    }
}