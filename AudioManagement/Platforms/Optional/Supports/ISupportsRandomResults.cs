using AudioManagement.Platforms.Errors;
using Result;

namespace AudioManagement.Platforms.Optional.Supports;

public interface ISupportsRandomResults
{
    public Task<Result<IEnumerable<PlatformResult>, SearchError>> GetRandomResults(int count);
}