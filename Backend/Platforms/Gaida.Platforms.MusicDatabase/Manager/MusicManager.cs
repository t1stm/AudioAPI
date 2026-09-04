using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gaida.Core.Utils;
using Serilog;

namespace Gaida.Platforms.MusicDatabase.Manager;

public partial class MusicManager(ILogger logger)
{
    protected readonly CoverExtractor CoverExtractor = new();
    protected List<MusicInfo> Songs = [];
    public ILogger Logger { get; } = logger;

    public static string Domain =>
        Environment.GetEnvironmentVariable("DOMAIN", EnvironmentVariableTarget.Process) ?? string.Empty;

    public static string StorageDirectory =>
        Environment.GetEnvironmentVariable("STORAGE", EnvironmentVariableTarget.Process) ?? "./";

    // TrimEnd because DOMAIN is written with a trailing slash as often as not, and concatenating it
    // straight onto "/Album_Covers" produced "https://host//Album_Covers/<hash>.jpg" — a URL that
    // renders in a browser but that Cover.cs's HttpClient fetch and any strict client both choke on.
    public static string AlbumCoverLocation => Domain.TrimEnd('/') + "/Album_Covers";

    public async Task Initialize()
    {
        Logger.Information("Initializing MusicManager");
        var storage = Environment.GetEnvironmentVariable("STORAGE", EnvironmentVariableTarget.Process);
        if (storage is not null)
        {
            Logger.Debug("Ensuring storage directory exists: {Storage}", storage);
            Directory.CreateDirectory(storage);
        }

        var albumCovers = Environment.GetEnvironmentVariable("ALBUM_COVERS", EnvironmentVariableTarget.Process);
        if (albumCovers is not null)
        {
            Logger.Debug("Ensuring album covers directory exists: {AlbumCovers}", albumCovers);
            Directory.CreateDirectory(albumCovers);
        }

        await Load();
        Logger.Debug("Extracting covers from {StorageDirectory}", StorageDirectory);
        CoverExtractor.Extract(StorageDirectory);
        Logger.Information("MusicManager initialization complete. Loaded {Count} songs", Songs.Count);
    }

    protected async Task Load()
    {
        Logger.Debug("Loading music from {StorageDirectory}", StorageDirectory);
        var folders = Directory.EnumerateDirectories(StorageDirectory, "*", SearchOption.AllDirectories).ToList();
        Logger.Debug("Found {Count} folders in storage", folders.Count);

        var parsed = new ConcurrentBag<List<MusicInfo>>();
        await Parallel.ForEachAsync(folders, async (folder, _) => parsed.Add(await ParseArtistFolder(folder)));

        // ponytail: folder order is no longer stable; nothing downstream depends on it (search scores, random shuffles).
        var songs = parsed.SelectMany(f => f).ToList();

        songs.ForEach(s => s.CoverUrl = s.CoverUrl?.Replace("$[DOMAIN]", AlbumCoverLocation));

        lock (Songs)
        {
            Songs = songs;
        }
    }

    private async Task<List<MusicInfo>> ParseArtistFolder(string artist)
    {
        Logger.Information("Loading artist: '{Artist}'", artist);
        var jsonFile = Path.Combine(artist, "Info.json");

        var songs = Directory.GetFiles(artist, "*", SearchOption.TopDirectoryOnly)
            .Where(song => IsAudioBasedOnFileExtension(song)).ToList();

        // Folders that only hold subfolders get no Info.json.
        if (songs.Count == 0 && !File.Exists(jsonFile)) return [];

        await using var fileStream = File.Open(jsonFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var existing = new List<MusicInfo>();
        if (fileStream.Length > 0)
            try
            {
                existing = await JsonSerializer.DeserializeAsync<List<MusicInfo>>(fileStream,
                    MusicInfo.SerializerOptions) ?? [];
            }
            catch (JsonException e)
            {
                Logger.Fatal(e, "Malformed Info.json for '{Artist}', rebuilding it", artist);
            }

        // Stale entries (e.g. .wvc WavPack correction files indexed by an older scanner) are never playable.
        var stale = existing.RemoveAll(m => m.RelativeLocation is null ||
                                            !IsAudioBasedOnFileExtension(m.RelativeLocation));
        if (stale > 0) Logger.Information("Dropped {Count} non-audio entries for '{Artist}'", stale, artist);

        // The four-field format took its names from the path, because the tags were read case-sensitively
        // and only .mp3 arrives lowercased. NewFiles never revisits an indexed song, so the re-read has to
        // happen here or those names stay path-derived forever.
        var legacy = existing.Where(entry => entry.WasLegacy).ToList();
        foreach (var entry in legacy) await RereadTags(entry);
        if (legacy.Count > 0)
            Logger.Information("Re-read tags for {Count} entries of '{Artist}'", legacy.Count, artist);

        var newFiles = NewFiles(existing, songs).ToList();
        if (stale == 0 && newFiles.Count == 0 && legacy.Count == 0) return existing;

        foreach (var file in newFiles)
            existing.Add(await ParseFile(file));

        fileStream.SetLength(0);
        fileStream.Position = 0;
        await JsonSerializer.SerializeAsync(fileStream, existing, MusicInfo.SerializerOptions);

        return existing;
    }

    private static async Task RereadTags(MusicInfo entry)
    {
        var path = StorageDirectory + "/" + entry.RelativeLocation;
        if (!File.Exists(path)) return;

        var tagged = await MediaInfo.GetInformation(path);
        entry.PreferTags(tagged);
        entry.ID = entry.UpdateRandomId();

        // The pipe deadlock in MediaInfo left a couple of entries with no duration at all, and the weak
        // match gates on it. The re-read is the one place that can repair them.
        if (entry.Duration == TimeSpan.Zero) entry.Duration = tagged.Duration;
    }

    private static IEnumerable<string> NewFiles(List<MusicInfo> existing, List<string> files)
    {
        return files.Where(location =>
            existing.All(m => m.RelativeLocation != RelativeLocation(location)));
    }

    private static string RelativeLocation(string location)
    {
        return Path.GetRelativePath(StorageDirectory, location);
    }

    private static async Task<MusicInfo> ParseFile(string location)
    {
        var split = location.Split('/');
        var filename = split[^1];
        var folder = split[^2];

        var filenameSplit = filename.Split(" - ");
        var author = filenameSplit[0];
        var title = string.Join('.',
            string.Join('-', filenameSplit[1..]).Split('.')[..^1]);

        // Tags first, then the filename, then the folder: the path spellings are kept as alternates rather
        // than discarded, so a folder typo costs a variant instead of the whole name.
        var entry = await MediaInfo.GetInformation(location);
        entry.AddNames(title, author, folder);
        entry.RelativeLocation ??= RelativeLocation(location);
        entry.ID = entry.UpdateRandomId();

        return entry;
    }

    protected static bool IsAudioBasedOnFileExtension(ReadOnlySpan<char> fileName)
    {
        return fileName.EndsWith(".flac") || fileName.EndsWith(".ogg") ||
               fileName.EndsWith(".mp3") || fileName.EndsWith(".wav") ||
               fileName.EndsWith(".mka") || fileName.EndsWith(".adts") ||
               fileName.EndsWith(".wma") || fileName.EndsWith(".wv");
    }

    /// <returns>Matching songs, empty when the term is unusable or nothing matches.</returns>
    public IEnumerable<MusicInfo> SearchByTerm(string term)
    {
        Logger.Debug("MusicManager: Searching by term: {Term}", term);
        var termClean = LevenshteinDistance.RemoveFormatting(
            ParentesisRegex().Replace(term, string.Empty));

        if (string.IsNullOrEmpty(termClean))
        {
            Logger.Information("MusicManager: Cleaned search term is empty for: {Term}", term);
            return [];
        }

        var found = Songs.Where(r => ScoreSingleTerm(termClean, r)).ToList();
        Logger.Debug("MusicManager: Found {Count} matches for term: {Term}", found.Count, term);
        return found;
    }

    public IEnumerable<MusicInfo> GetRandomSongs(int count)
    {
        Logger.Debug("MusicManager: Getting {Count} random songs", count);
        var songs = Songs.ToArray();
        Random.Shared.Shuffle(songs);
        return songs.Take(count);
    }

    /// <summary>Every title against every artist, in both orders — the arrays are what the entry can be found by.</summary>
    private static bool ScoreSingleTerm(string termClean, MusicInfo r)
    {
        var (titles, artists, _) = r.Search;

        return artists.Any(artist => LevenshteinDistance.ComputeStrict(artist, termClean) < 2)
               || titles.Any(title => LevenshteinDistance.ComputeStrict(title, termClean) < 2)
               || titles.Any(title => artists.Any(artist =>
                   LevenshteinDistance.ComputeStrict(title + artist, termClean) < 3 ||
                   LevenshteinDistance.ComputeStrict(artist + title, termClean) < 3));
    }

    /// <returns>The song, or <c>null</c> when the ID isn't known.</returns>
    public MusicInfo? SearchById(string id)
    {
        Logger.Debug("MusicManager: Searching by ID: {Id}", id);
        var search = Songs.AsParallel().FirstOrDefault(r => r.ID == id);

        // Second pass for regenerated infos, whose last two characters are re-rolled.
        if (search is null && id.Length > 2)
            search = Songs.AsParallel().FirstOrDefault(r => r.ID?.Length > 2 && r.ID[..^2] == id[..^2]);

        if (search is null)
        {
            Logger.Information("MusicManager: ID not found: {Id}", id);
            return null;
        }

        Logger.Debug("MusicManager: Found song for ID {Id}: {Title}", id, search.Title);
        return search;
    }

    /// <summary>
    ///     The library as the folder tree it already is on disk: one level of it, so the client asks for the
    ///     next level only when someone opens it.
    /// </summary>
    /// <param name="path">Folder relative to the storage root; empty or "/" for the root itself.</param>
    /// <returns>The immediate subfolders with the songs beneath each, and the songs directly in the folder.</returns>
    public (List<(string Name, int Songs)> Folders, List<MusicInfo> Files) Browse(string? path)
    {
        // Nothing here touches the filesystem: the path is only ever compared against the RelativeLocation
        // strings already in memory, so "../" matches no prefix and escapes nothing. Separators are '/'
        // because ParseFile splits on '/' too — a backslash is normalized rather than supported.
        var prefix = (path ?? string.Empty).Replace('\\', '/').Trim('/');
        if (prefix.Length > 0) prefix += "/";

        var folders = new Dictionary<string, int>(StringComparer.Ordinal);
        var files = new List<MusicInfo>();

        foreach (var song in Songs)
        {
            if (song.RelativeLocation is not { } location ||
                !location.StartsWith(prefix, StringComparison.Ordinal)) continue;

            var rest = location[prefix.Length..];
            var slash = rest.IndexOf('/');

            if (slash < 0) files.Add(song);
            else folders[rest[..slash]] = folders.GetValueOrDefault(rest[..slash]) + 1;
        }

        Logger.Debug("MusicManager: Browsed '{Path}': {Folders} folders, {Files} files", prefix, folders.Count,
            files.Count);

        // ponytail: one linear scan of the song list per request. 3671 entries is sub-millisecond and a prefix
        // index would need invalidating on every rescan — build one only if the library passes six figures.
        return ([
                .. folders.OrderBy(folder => folder.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(folder => (Name: folder.Key, Songs: folder.Value))
            ],
            [
                .. files.OrderBy(song => song.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            ]);
    }

    [GeneratedRegex(@"\(.*?\)")]
    private static partial Regex ParentesisRegex();

    private static bool IsArtistPartOfSong(string artist, MusicInfo song)
    {
        return song.Search.Artists.Any(name => name.Contains(artist, StringComparison.Ordinal));
    }

    /// <returns>The artist's songs, empty when the name is unusable or nothing matches.</returns>
    public IEnumerable<MusicInfo> GetArtistSongs(string artist)
    {
        Logger.Debug("MusicManager: Getting songs for artist: {Artist}", artist);
        var artistRemovedFormatting = LevenshteinDistance.RemoveFormatting(artist);
        if (string.IsNullOrEmpty(artistRemovedFormatting))
        {
            Logger.Information("MusicManager: Cleaned artist name is empty for: {Artist}", artist);
            return [];
        }

        var artistSongs = Songs.AsParallel()
            .Where(song => IsArtistPartOfSong(artistRemovedFormatting, song)).ToList();

        Logger.Debug("MusicManager: Found {Count} songs for artist: {Artist}", artistSongs.Count, artist);
        return artistSongs;
    }
}