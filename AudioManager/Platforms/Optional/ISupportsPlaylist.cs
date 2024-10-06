using AudioManager.Platforms.Errors;
using Result;

namespace AudioManager.Platforms.Optional;

public interface ISupportsPlaylist
{
    public Task<Result<IEnumerable<PlatformResult>, SearchError>> TrySearchPlaylist(string playlist,
        CancellationToken cancellation_token = default);

    public bool IsPlaylistUrl(string query);
}