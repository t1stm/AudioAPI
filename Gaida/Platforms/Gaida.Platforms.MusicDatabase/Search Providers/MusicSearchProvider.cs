using Gaida.Core.Platforms;
using Gaida.Core.Platforms.Optional.Supports;
using Gaida.Core.Utils;
using Gaida.Platforms.MusicDatabase.Manager;
using Serilog;

namespace Gaida.Platforms.MusicDatabase.Search_Providers;

public class MusicSearchProvider(ILogger logger) : SearchProvider(logger),
    ISupportsID, ISupportsSearch, ISupportsRandomResults
{
    protected readonly MusicManager MusicManager = new(logger);
    public override string PlatformIdentifier => "audio://";
    public override int Priority => 99;

    public Task<PlatformResult?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var found = MusicManager.SearchById(id);
        return Task.FromResult<PlatformResult?>(found?.ToMusicResult(ContentDownloaders));
    }

    public IAsyncEnumerable<PlatformResult> GetRandomResults(int count,
        CancellationToken cancellationToken = default)
    {
        return ToResults(MusicManager.GetRandomSongs(count));
    }

    public IAsyncEnumerable<PlatformResult> SearchKeywords(string keywords,
        CancellationToken cancellationToken = default)
    {
        return ToResults(MusicManager.SearchByTerm(keywords));
    }

    public IAsyncEnumerable<PlatformResult> GetArtistSongs(string artist)
    {
        return ToResults(MusicManager.GetArtistSongs(artist));
    }

    /// <returns>One level of the library's folder tree: subfolders with their song counts, and the songs in the folder.</returns>
    public (IReadOnlyList<(string Name, int Songs)> Folders, IReadOnlyList<PlatformResult> Files) Browse(string? path)
    {
        var (folders, files) = MusicManager.Browse(path);
        return (folders, [.. files.Select(PlatformResult (song) => song.ToMusicResult(ContentDownloaders))]);
    }

    public (LocalMatch Match, PlatformResult Result)? FindLocalVariant(string name, string? artist, TimeSpan duration)
    {
        var match = MusicManager.FindLocalVariant(name, artist, duration);
        return match is null ? null : (match, match.Song.ToMusicResult(ContentDownloaders));
    }

    protected override void Initialize()
    {
        Logger.Debug("Initializing MusicSearchProvider");
        _ = MusicManager.Initialize();
        base.Initialize();
    }

    private IAsyncEnumerable<PlatformResult> ToResults(IEnumerable<MusicInfo> songs)
    {
        return songs.Select(PlatformResult (song) => song.ToMusicResult(ContentDownloaders)).AsAsync();
    }
}