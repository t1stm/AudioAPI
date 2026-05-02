using AudioManagement.Platforms.Errors;
using AudioManagement.Platforms.MusicDatabase.Getters;
using AudioManagement.Platforms.MusicDatabase.Search_Providers;
using AudioManagement.Platforms.Optional.Supports;
using Result;
using Serilog;

namespace AudioManagement.Platforms.MusicDatabase;

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
        var provider = (MusicSearchProvider)SearchProviders[0];
        return provider.GetRandomResults(count);
    }

    public Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchKeywords(string keywords,
        CancellationToken cancellationToken = default)
    {
        var provider = (MusicSearchProvider)SearchProviders[0];
        return provider.TrySearchKeywords(keywords, cancellationToken);
    }

    public override Task<Result<PlatformResult, SearchError>> TryID(string id,
        CancellationToken cancellationToken = default)
    {
        var provider = (MusicSearchProvider)SearchProviders[0];
        return provider.TryID(id, cancellationToken);
    }

    public Task<Result<IEnumerable<PlatformResult>, SearchError>> GetArtistSongs(string artist)
    {
        var provider = (MusicSearchProvider)SearchProviders[0];
        return provider.GetArtistSongs(artist);
    }
}