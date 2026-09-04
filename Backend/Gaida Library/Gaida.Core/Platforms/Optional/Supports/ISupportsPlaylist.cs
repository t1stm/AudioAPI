namespace Gaida.Core.Platforms.Optional.Supports;

public interface ISupportsPlaylist
{
    /// <returns>Playlist entries as they arrive. An empty sequence means nothing was found.</returns>
    public IAsyncEnumerable<PlatformResult> SearchPlaylist(string playlist,
        CancellationToken cancellationToken = default);
}