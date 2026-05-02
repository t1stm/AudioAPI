using AudioManagement.Platforms.Errors;
using Result;

namespace AudioManagement.Platforms.Optional.Supports;

public interface ISupportsID
{
    public Task<Result<PlatformResult, SearchError>> TryID(string id,
        CancellationToken cancellation_token = default);
}