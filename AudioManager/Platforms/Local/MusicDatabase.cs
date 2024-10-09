using AudioManager.Platforms.Errors;
using AudioManager.Platforms.Local.Getters;
using AudioManager.Platforms.Local.Search_Providers;
using AudioManager.Platforms.Optional.Supports;
using Result;

namespace AudioManager.Platforms.Local;

public class MusicDatabase : Platform, ISupportsSearch
{
    public override HashSet<string> SearchIDIdentifiers { get; } = ["audio://"];
    public override HashSet<string> PlatformDomains { get; } = [];
    public override HashSet<string> SearchPlaylistIdentifiers { get; } = [];
    
    public override string Name => "Music Database";
    public override string Description => "Locally stored music";
    public override int Priority => 99;
    protected override List<SearchProvider> SearchProviders { get; set; } = [
        new MusicSearchProvider()
    ];
    protected override List<ContentGetter> ContentDownloaders { get; set; } = [
        new MusicGetter()
    ];

    public override Task<Result<PlatformResult, SearchError>> TryID(string id, CancellationToken cancellation_token = default)
    {
        var provider = (MusicSearchProvider)SearchProviders[0];
        return provider.TryID(id, cancellation_token);
    }

    public Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchKeywords(string keywords, CancellationToken cancellation_token = default)
    {
        var provider = (MusicSearchProvider)SearchProviders[0];
        return provider.TrySearchKeywords(keywords, cancellation_token);
    }
}