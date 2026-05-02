using AudioManagement.Platforms.Errors;
using AudioManagement.Platforms.Optional.Supports;
using Result;
using Result.Objects;
using Serilog;

namespace AudioManagement.Platforms;

public abstract class Platform(ILogger logger) : ISupportsID
{
    public ILogger Logger { get; } = logger.ForContext<Platform>();
    protected abstract HashSet<string> SearchIDIdentifiers { get; }
    protected abstract HashSet<string> PlatformDomains { get; }
    protected abstract HashSet<string> SearchPlaylistIdentifiers { get; }

    public virtual HashSet<string>.AlternateLookup<ReadOnlySpan<char>> SearchIDIdentifiersLookup =>
        SearchIDIdentifiers.GetAlternateLookup<ReadOnlySpan<char>>();

    public virtual HashSet<string>.AlternateLookup<ReadOnlySpan<char>> PlatformDomainsLookup =>
        PlatformDomains.GetAlternateLookup<ReadOnlySpan<char>>();

    public virtual HashSet<string>.AlternateLookup<ReadOnlySpan<char>> SearchPlaylistIdentifiersLookup =>
        SearchPlaylistIdentifiers.GetAlternateLookup<ReadOnlySpan<char>>();

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract int Priority { get; }

    protected abstract List<SearchProvider> SearchProviders { get; set; }
    protected abstract List<ContentGetter> ContentDownloaders { get; set; }

    public virtual async Task<Result<PlatformResult, SearchError>> TryID(string id,
        CancellationToken cancellationToken = default)
    {
        foreach (var searchProvider in
                 SearchProviders.OfType<ISupportsID>())
        {
            var result = await searchProvider.TryID(id, cancellationToken);
            if (result == Status.Ok) return result;
        }

        return Result<PlatformResult, SearchError>.Error(default);
    }

    public virtual void Initialize()
    {
        SortProviders();
        SetupSearchProviders();
    }

    protected virtual void SortProviders()
    {
        SearchProviders = SearchProviders.OrderByDescending(x => x.Priority).ToList();
        ContentDownloaders = ContentDownloaders.OrderByDescending(x => x.Priority).ToList();
    }

    protected virtual void SetupSearchProviders()
    {
        SearchProviders.ForEach(s => s.RegisterContentDownloaders(ContentDownloaders));
    }
}