using Gaida.Core.Platforms;
using Gaida.Core.Platforms.Optional.Supports;
using Gaida.Platforms.MusicDatabase.Getters;
using Gaida.Platforms.MusicDatabase.Manager;
using Gaida.Platforms.MusicDatabase.Search_Providers;
using Serilog;

namespace Gaida.Platforms.MusicDatabase;

public class MusicDatabase : Platform, ISupportsSearch, ISupportsRandomResults
{
    private readonly MusicSearchProvider _provider;

    public MusicDatabase(ILogger logger) : base(logger)
    {
        _provider = new MusicSearchProvider(logger);

        SearchProviders = [_provider];
        ContentDownloaders = [new MusicGetter(logger)];
    }

    protected override HashSet<string> SearchIDIdentifiers { get; } = ["audio://"];
    protected override HashSet<string> SearchPlaylistIdentifiers { get; } = [];

    protected override List<SearchProvider> SearchProviders { get; set; }
    protected override List<ContentGetter> ContentDownloaders { get; set; }

    public IAsyncEnumerable<PlatformResult> GetRandomResults(int count,
        CancellationToken cancellationToken = default)
    {
        return _provider.GetRandomResults(count, cancellationToken);
    }

    public IAsyncEnumerable<PlatformResult> SearchKeywords(string keywords,
        CancellationToken cancellationToken = default)
    {
        return _provider.SearchKeywords(keywords, cancellationToken);
    }

    public IAsyncEnumerable<PlatformResult> GetArtistSongs(string artist)
    {
        return _provider.GetArtistSongs(artist);
    }

    /// <returns>The library's answer to a YouTube title, or <c>null</c> when it has none worth offering.</returns>
    public (LocalMatch Match, PlatformResult Result)? FindLocalVariant(string name, string? artist, TimeSpan duration)
    {
        return _provider.FindLocalVariant(name, artist, duration);
    }
}
