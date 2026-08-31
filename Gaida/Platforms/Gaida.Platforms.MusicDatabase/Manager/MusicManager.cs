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

    public static string AlbumCoverLocation => Domain + "/Album_Covers";

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

        var newFiles = NewFiles(existing, songs).ToList();
        if (stale == 0 && newFiles.Count == 0) return existing;

        foreach (var file in newFiles)
            existing.Add(await ParseFile(file));

        fileStream.SetLength(0);
        fileStream.Position = 0;
        await JsonSerializer.SerializeAsync(fileStream, existing, MusicInfo.SerializerOptions);

        return existing;
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
        var romanizedAuthor = split[^2];

        var filenameSplit = filename.Split(" - ");
        var author = filenameSplit[0];
        var title = string.Join('.',
            string.Join('-', filenameSplit[1..]).Split('.')[..^1]);

        var entry = await MediaInfo.GetInformation(location);
        entry.OriginalTitle ??= title.Trim();
        entry.OriginalAuthor ??= author.Trim();
        entry.RomanizedTitle ??= Romanize.FromCyrillic(title).Trim();
        entry.RomanizedAuthor ??= romanizedAuthor.Trim();
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

    private static bool ScoreSingleTerm(string termClean, MusicInfo r)
    {
        var romanizedTitleClean = r.RomanizedTitle is null
            ? null
            : LevenshteinDistance.RemoveFormatting(ParentesisRegex().Replace(r.RomanizedTitle, string.Empty));

        var originalTitleClean = r.OriginalTitle is null
            ? null
            : LevenshteinDistance.RemoveFormatting(ParentesisRegex().Replace(r.OriginalTitle, string.Empty));

        var romanizedArtistClean =
            r.RomanizedAuthor is null ? null : LevenshteinDistance.RemoveFormatting(r.RomanizedAuthor);

        var originalArtistClean =
            r.OriginalAuthor is null ? null : LevenshteinDistance.RemoveFormatting(r.OriginalAuthor);

        var eval =
            (romanizedArtistClean != null &&
             LevenshteinDistance.ComputeStrict(romanizedArtistClean, termClean) < 2)
            ||
            (originalArtistClean != null &&
             LevenshteinDistance.ComputeStrict(originalArtistClean, termClean) < 2)
            ||
            (romanizedTitleClean != null &&
             (LevenshteinDistance.ComputeStrict(romanizedTitleClean, termClean) < 2 ||
              LevenshteinDistance.ComputeStrict($"{romanizedTitleClean}{romanizedArtistClean}", termClean) < 3 ||
              LevenshteinDistance.ComputeStrict($"{romanizedArtistClean}{romanizedTitleClean}", termClean) < 3 ||
              LevenshteinDistance.ComputeStrict($"{romanizedTitleClean}{originalArtistClean}", termClean) < 3))
            ||
            (originalTitleClean != null &&
             (LevenshteinDistance.ComputeStrict(originalTitleClean, termClean) < 2 ||
              LevenshteinDistance.ComputeStrict($"{originalTitleClean}{originalArtistClean}", termClean) < 3 ||
              LevenshteinDistance.ComputeStrict($"{originalArtistClean}{originalTitleClean}", termClean) < 3 ||
              LevenshteinDistance.ComputeStrict($"{originalTitleClean}{romanizedArtistClean}", termClean) < 3));
        return eval;
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

        Logger.Debug("MusicManager: Found song for ID {Id}: {Title}", id, search.OriginalTitle);
        return search;
    }

    [GeneratedRegex(@"\(.*?\)")]
    private static partial Regex ParentesisRegex();

    private static bool IsArtistPartOfSong(string artist, MusicInfo song)
    {
        var songArtistFormatted = LevenshteinDistance.RemoveFormatting(song.OriginalAuthor) ?? "";
        var songArtistRomanized = LevenshteinDistance.RemoveFormatting(song.RomanizedAuthor) ?? "";

        ReadOnlySpan<char> artistSpan = artist;
        ReadOnlySpan<char> formattedSpan = songArtistFormatted;
        ReadOnlySpan<char> romanizedSpan = songArtistRomanized;

        return (formattedSpan.Length != 0 || romanizedSpan.Length != 0) &&
               (formattedSpan.IndexOf(artistSpan) != -1 || romanizedSpan.IndexOf(artistSpan) != -1);
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
