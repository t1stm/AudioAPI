namespace Gaida.Core.Platforms.Optional.Supports;

public interface ISupportsRandomResults
{
    public IAsyncEnumerable<PlatformResult> GetRandomResults(int count,
        CancellationToken cancellationToken = default);
}