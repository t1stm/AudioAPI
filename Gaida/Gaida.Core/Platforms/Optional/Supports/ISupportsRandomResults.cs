using Gaida.Core.Platforms.Errors;
using Result;

namespace Gaida.Core.Platforms.Optional.Supports;

public interface ISupportsRandomResults
{
    public Task<Result<IEnumerable<PlatformResult>, SearchError>> GetRandomResults(int count);
}