using Gaida.Core.Platforms.Optional.Supports;
using Serilog;

namespace Gaida.Core.Platforms;

public abstract class Platform : ISupportsID
{
    protected Platform(ILogger logger)
    {
        Logger = logger.ForContext(GetType());
    }

    protected ILogger Logger { get; }

    protected abstract HashSet<string> SearchIDIdentifiers { get; }
    protected abstract HashSet<string> SearchPlaylistIdentifiers { get; }

    public virtual HashSet<string>.AlternateLookup<ReadOnlySpan<char>> SearchIDIdentifiersLookup =>
        SearchIDIdentifiers.GetAlternateLookup<ReadOnlySpan<char>>();

    public virtual HashSet<string>.AlternateLookup<ReadOnlySpan<char>> SearchPlaylistIdentifiersLookup =>
        SearchPlaylistIdentifiers.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>Whether this platform recognises the query as one of its playlist URLs.</summary>
    public virtual bool IsPlaylistUrl(ReadOnlySpan<char> query)
    {
        return false;
    }

    protected abstract List<SearchProvider> SearchProviders { get; set; }
    protected abstract List<ContentGetter> ContentDownloaders { get; set; }

    public virtual async Task<PlatformResult?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        foreach (var searchProvider in SearchProviders.OfType<ISupportsID>())
            try
            {
                var result = await searchProvider.GetByIdAsync(id, cancellationToken);
                if (result is not null) return result;
            }
            catch (Exception e)
            {
                Logger.Error(e, "Provider {Provider} failed for ID {ID}", searchProvider.GetType().Name, id);
            }

        return null;
    }

    public virtual void Initialize()
    {
        SearchProviders = SearchProviders.OrderByDescending(x => x.Priority).ToList();
        ContentDownloaders = ContentDownloaders.OrderByDescending(x => x.Priority).ToList();

        SearchProviders.ForEach(s => s.RegisterContentDownloaders(ContentDownloaders));
    }
}
