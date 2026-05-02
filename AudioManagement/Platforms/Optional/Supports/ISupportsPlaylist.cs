using AudioManagement.Platforms.Errors;
using Result;

namespace AudioManagement.Platforms.Optional.Supports;

public interface ISupportsPlaylist
{
    public Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchPlaylist(string playlist,
        CancellationToken cancellationToken = default);

    public bool IsPlaylistUrl(ReadOnlySpan<char> query);
}