using AudioManagement.Platforms.Errors;
using Result;

namespace AudioManagement.Platforms.Optional.Supports;

public interface ISupportsPlaylist
{
    public Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchPlaylist(string playlist,
        CancellationToken cancellation_token = default);

    public bool IsPlaylistUrl(ReadOnlySpan<char> query);
}