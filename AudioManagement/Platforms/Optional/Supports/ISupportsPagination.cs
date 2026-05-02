using AudioManagement.Platforms.Errors;
using Result;

namespace AudioManagement.Platforms.Optional.Supports;

public interface ISupportsPaginatedSearch
{
    public Task<Result<IEnumerable<PlatformResult>, SearchError>> TryPaginatedSearch(string keywords,
        int page = 1, int pageSize = 10,
        CancellationToken cancellationToken = default);
}