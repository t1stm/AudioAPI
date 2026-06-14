using Gaida.Core.Platforms.Errors;
using Result;

namespace Gaida.Core.Platforms.Optional.Supports;

public interface ISupportsSearch
{
    public Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchKeywords(string keywords,
        CancellationToken cancellationToken = default);
}