using AudioManager.Platforms.Errors;
using Result;

namespace AudioManager.Platforms.Optional.Supports;

public interface ISupportsPaginatedSearch
{
    public Task<Result<IEnumerable<PlatformResult>, SearchError>> TryPaginatedSearch(string keywords,
        int page = 1, int page_size = 10,
        CancellationToken cancellation_token = default);
}