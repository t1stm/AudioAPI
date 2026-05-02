using System.Text;
using System.Text.RegularExpressions;
using AudioManagement.Utils;
using Newtonsoft.Json;
using Result;
using Result.Objects;
using Serilog;

namespace AudioManagement.Platforms.MusicDatabase.Manager;

public partial class MusicManager(ILogger logger)
{
    public ILogger Logger { get; } = logger;
    protected readonly CoverExtractor CoverExtractor = new();
    protected List<MusicInfo> Songs = [];

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
        var songs = new List<MusicInfo>();
        var genres = Directory.EnumerateDirectories(StorageDirectory, "*", SearchOption.TopDirectoryOnly).ToList();

        Logger.Debug("Found {Count} genres in storage", genres.Count);
        foreach (var genre in genres)
        {
            var artists = Directory.EnumerateDirectories(genre, "*", SearchOption.TopDirectoryOnly).ToList();
            Logger.Debug("Found {Count} artists in genre {Genre}", artists.Count, genre);
            foreach (var artist in artists)
            {
                var process = await ParseArtistFolder(artist);
                songs.AddRange(process);
            }
        }

        songs.ForEach(s => s.CoverUrl = s.CoverUrl?.Replace("$[DOMAIN]", AlbumCoverLocation));

        lock (Songs)
        {
            Songs = songs;
        }
    }

    private async Task<IEnumerable<MusicInfo>> ParseArtistFolder(string artist)
    {
        Logger.Information("Loading artist: '{Artist}'", artist);
        var artistName = artist.Split(Path.PathSeparator)[^1];
        var jsonFile = Path.Combine(artist, "Info.json");

        var songs = Directory.GetFiles($"{artist}", "*", SearchOption.TopDirectoryOnly)
            .Where(song => IsAudioBasedOnFileExtension(song)).ToList();

        var serializer = new JsonSerializer
        {
            Formatting = Formatting.Indented,
            StringEscapeHandling = StringEscapeHandling.EscapeHtml
        };

        await using var fileStream = File.Open(jsonFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        if (File.Exists(jsonFile))
        {
            using var sr = new StreamReader(fileStream, Encoding.UTF8, true, 4096, true);
            var json = await sr.ReadToEndAsync();

            try
            {
                var items = JsonConvert.DeserializeObject<List<MusicInfo>>(json) ??
                            Enumerable.Empty<MusicInfo>().ToList();
                if (items.Count == songs.Count) return items;

                var update = UpdateData(items, songs);
                fileStream.SetLength(0);
                fileStream.Flush();
                fileStream.Seek(0, SeekOrigin.Begin);

                var blocking = update.ToBlockingEnumerable();

                await using var writer = new StreamWriter(fileStream, Encoding.UTF8);
                serializer.Serialize(writer, blocking);

                return blocking;
            }
            catch (Exception e)
            {
                Log.Fatal("Error thrown for '{Artist}': '{@Exception}'", artist, e);
            }
        }

        {
            var list = songs.Where(song => IsAudioBasedOnFileExtension(song))
                .Select(ParseFile);

            var awaited = await Task.WhenAll(list);
            await using var writer = new StreamWriter(fileStream, Encoding.UTF8);

            serializer.Serialize(writer, awaited);
            fileStream.Close();

            return awaited;
        }
    }

    private static async IAsyncEnumerable<MusicInfo> UpdateData(List<MusicInfo> existing, List<string> files)
    {
        foreach (var info in existing) yield return info;

        var newFiles = files.Where(location =>
            IsAudioBasedOnFileExtension(location) &&
            existing.All(m =>
            {
                var relativeLocation = string.Join('/', location.Split('/')[^3..]);
                return m.RelativeLocation != relativeLocation;
            }));

        foreach (var file in newFiles)
            yield return await ParseFile(file);
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
        entry.RelativeLocation ??= string.Join('/', split[^3..]);
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

    public Result<IEnumerable<MusicInfo>, Empty> SearchByTerm(string term)
    {
        Logger.Debug("MusicManager: Searching by term: {Term}", term);
        var termClean = LevenshteinDistance.RemoveFormatting(
            ParentesisRegex().Replace(term, string.Empty));

        if (string.IsNullOrEmpty(termClean))
        {
            Logger.Information("MusicManager: Cleaned search term is empty for: {Term}", term);
            return Result<IEnumerable<MusicInfo>, Empty>.Error(new Empty());
        }

        var found = Songs.Where(r => ScoreSingleTerm(termClean, r)).ToList();
        Logger.Debug("MusicManager: Found {Count} matches for term: {Term}", found.Count, term);
        return Result<IEnumerable<MusicInfo>, Empty>.Success(found);
    }

    public Result<IEnumerable<MusicInfo>, Empty> GetRandomSongs(int count)
    {
        Logger.Debug("MusicManager: Getting {Count} random songs", count);
        var songs = Songs.OrderBy(_ => Guid.NewGuid()).Take(count).ToList();
        return Result<IEnumerable<MusicInfo>, Empty>.Success(songs);
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

    public Result<MusicInfo, Empty> SearchById(string id)
    {
        Logger.Debug("MusicManager: Searching by ID: {Id}", id);
        var search = Songs.AsParallel().FirstOrDefault(r => r.ID == id) ??
                     // Second pass for regenerated infos.
                     Songs.AsParallel().FirstOrDefault(r => (r.ID ?? "  ")[..^2] == id[..^2]);

        if (search == null)
        {
            Logger.Information("MusicManager: ID not found: {Id}", id);
            return Result<MusicInfo, Empty>.Error(default);
        }

        Logger.Debug("MusicManager: Found song for ID {Id}: {Title}", id, search.OriginalTitle);
        return Result<MusicInfo, Empty>.Success(search);
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

    public Result<IEnumerable<MusicInfo>, Empty> GetArtistSongs(string artist)
    {
        Logger.Debug("MusicManager: Getting songs for artist: {Artist}", artist);
        var artistRemovedFormatting = LevenshteinDistance.RemoveFormatting(artist);
        if (string.IsNullOrEmpty(artistRemovedFormatting))
        {
            Logger.Information("MusicManager: Cleaned artist name is empty for: {Artist}", artist);
            return Result<IEnumerable<MusicInfo>, Empty>.Error(default);
        }

        var artistSongs = Songs.AsParallel()
            .Where(song => IsArtistPartOfSong(artistRemovedFormatting, song)).ToList();

        Logger.Debug("MusicManager: Found {Count} songs for artist: {Artist}", artistSongs.Count, artist);
        return Result<IEnumerable<MusicInfo>, Empty>.Success(artistSongs);
    }
}