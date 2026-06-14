using Gaida.Core.Platforms.Errors;
using Result;

namespace Gaida.Core.Platforms.Optional.Supports;

public interface ISupportsID
{
    public Task<Result<PlatformResult, SearchError>> TryID(string id,
        CancellationToken cancellationToken = default);
}