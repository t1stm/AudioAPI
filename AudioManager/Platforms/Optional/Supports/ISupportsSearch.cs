using AudioManager.Platforms.Errors;
using Result;

namespace AudioManager.Platforms.Optional.Supports;

public interface ISupportsSearch
{
    public Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchKeywords(string keywords,
        CancellationToken cancellation_token = default);
}