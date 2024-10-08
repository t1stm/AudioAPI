using AudioManager.Platforms.Errors;
using Result;

namespace AudioManager.Platforms.Optional.Supports;

public interface ISupportsID
{
    public Task<Result<PlatformResult, SearchError>> TryID(string id,
        CancellationToken cancellation_token = default);
}