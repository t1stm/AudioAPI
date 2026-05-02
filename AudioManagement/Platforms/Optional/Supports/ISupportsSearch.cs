using AudioManagement.Platforms.Errors;
using Result;

namespace AudioManagement.Platforms.Optional.Supports;

public interface ISupportsSearch
{
    public Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchKeywords(string keywords,
        CancellationToken cancellation_token = default);
}