namespace Gaida.Core.Platforms.Optional.Supports;

public interface ISupportsSearch
{
    /// <returns>Results as they arrive. An empty sequence means nothing was found.</returns>
    public IAsyncEnumerable<PlatformResult> SearchKeywords(string keywords,
        CancellationToken cancellationToken = default);
}
