using AudioManager.Platforms.Errors;
using Result;

namespace AudioManager.Platforms.Optional.Supports;

public interface ISupportsRandomResults
{
    public Task<Result<IEnumerable<PlatformResult>, SearchError>> GetRandomResults(int count);
}