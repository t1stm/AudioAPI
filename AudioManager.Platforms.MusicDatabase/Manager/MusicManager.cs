using System.Text;
using System.Text.RegularExpressions;
using Audio.Utils;
using Newtonsoft.Json;
using Result;
using Result.Objects;

namespace AudioManager.Platforms.MusicDatabase.Manager;

public partial class MusicManager
{
    public static string Domain => Environment.GetEnvironmentVariable("DOMAIN", EnvironmentVariableTarget.Process) ?? string.Empty;
    public static string StorageDirectory => Environment.GetEnvironmentVariable("STORAGE", EnvironmentVariableTarget.Process) ?? "./";
    public static string AlbumCoverLocation => Domain + "/Album_Covers";

    protected readonly CoverExtractor CoverExtractor = new();
    protected List<MusicInfo> Songs = [];

    public async Task Initialize()
    {
        var storage = Environment.GetEnvironmentVariable("STORAGE", EnvironmentVariableTarget.Process);
        if (storage is not null)
        {
            Directory.CreateDirectory(storage);
        }

        var album_covers = Environment.GetEnvironmentVariable("ALBUM_COVERS", EnvironmentVariableTarget.Process);
        if (album_covers is not null)
        {
            Directory.CreateDirectory(album_covers);
        }

        await Load();
        CoverExtractor.Extract(StorageDirectory);
    }

    protected async Task Load()
    {
        var songs = new List<MusicInfo>();
        var genres = Directory.EnumerateDirectories(StorageDirectory, "*", SearchOption.TopDirectoryOnly);

        foreach (var genre in genres)
        {
            var artists = Directory.EnumerateDirectories(genre, "*", SearchOption.TopDirectoryOnly);
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

    private static async Task<IEnumerable<MusicInfo>> ParseArtistFolder(string artist)
    {
        Console.WriteLine($"Loading artist: \'{artist}\'");
        var artist_name = artist.Split(Path.PathSeparator)[^1];
        var json_file = Path.Combine(artist, "Info.json");

        var songs = Directory.GetFiles($"{artist}", "*", SearchOption.TopDirectoryOnly)
            .Where(song => IsAudioBasedOnFileExtension(song)).ToList();

        var serializer = new JsonSerializer
        {
            Formatting = Formatting.Indented,
            StringEscapeHandling = StringEscapeHandling.EscapeHtml
        };

        await using var file_stream = File.Open(json_file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        if (File.Exists(json_file))
        {
            using var sr = new StreamReader(file_stream, Encoding.UTF8, true, 4096, true);
            var json = await sr.ReadToEndAsync();

            try
            {
                var items = JsonConvert.DeserializeObject<List<MusicInfo>>(json) ??
                            Enumerable.Empty<MusicInfo>().ToList();
                if (items.Count == songs.Count) return items;

                var update = UpdateData(items, songs);
                file_stream.SetLength(0);
                file_stream.Flush();
                file_stream.Seek(0, SeekOrigin.Begin);

                var blocking = update.ToBlockingEnumerable();
                
                await using var writer = new StreamWriter(file_stream, Encoding.UTF8);
                serializer.Serialize(writer, blocking);

                return blocking;
            }
            catch (Exception e)
            {
                // TODO: log with logger
                Console.WriteLine($"Error thrown for \'{artist}\': \'{e}\'");
            }
        }

        {
            var list = songs.Where(song => IsAudioBasedOnFileExtension(song))
                .Select(ParseFile);

            var awaited = await Task.WhenAll(list);
            await using var writer = new StreamWriter(file_stream, Encoding.UTF8);

            serializer.Serialize(writer, awaited);
            file_stream.Close();

            return awaited;
        }
    }

    private static async IAsyncEnumerable<MusicInfo> UpdateData(List<MusicInfo> existing, List<string> files)
    {
        foreach (var info in existing) yield return info;

        var new_files = files.Where(location =>
            IsAudioBasedOnFileExtension(location) &&
            existing.All(m =>
            {
                var relative_location = string.Join('/', location.Split('/')[^3..]);
                return m.RelativeLocation != relative_location;
            }));

        foreach (var file in new_files) 
            yield return await ParseFile(file);
    }

    private static async Task<MusicInfo> ParseFile(string location)
    {
        var split = location.Split('/');
        var filename = split[^1];
        var romanized_author = split[^2];

        var filename_split = filename.Split(" - ");
        var author = filename_split[0];
        var title = string.Join('.',
            string.Join('-', filename_split[1..]).Split('.')[..^1]);

        var entry = await MediaInfo.GetInformation(location);
        entry.OriginalTitle ??= title.Trim();
        entry.OriginalAuthor ??= author.Trim();
        entry.RomanizedTitle ??= Romanize.FromCyrillic(title).Trim();
        entry.RomanizedAuthor ??= romanized_author.Trim();
        entry.RelativeLocation ??= string.Join('/', split[^3..]);
        entry.ID = entry.UpdateRandomId();

        return entry;
    }

    protected static bool IsAudioBasedOnFileExtension(ReadOnlySpan<char> file_name)
    {
        return file_name.EndsWith(".flac") || file_name.EndsWith(".ogg") ||
               file_name.EndsWith(".mp3") || file_name.EndsWith(".wav") ||
               file_name.EndsWith(".mka") || file_name.EndsWith(".adts") ||
               file_name.EndsWith(".wma") || file_name.EndsWith(".wv");
    }

    public Result<IEnumerable<MusicInfo>, Empty> SearchByTerm(string term)
    {
        var term_clean = LevenshteinDistance.RemoveFormatting(
            ParentesisRegex().Replace(term, string.Empty));
        
        if (string.IsNullOrEmpty(term_clean))
            return Result<IEnumerable<MusicInfo>, Empty>.Error(new Empty());
        
        var found = Songs.Where(r => ScoreSingleTerm(term_clean, r));
        return Result<IEnumerable<MusicInfo>, Empty>.Success(found);
    }

    public Result<IEnumerable<MusicInfo>, Empty> GetRandomSongs(int count)
    {
        var songs = Songs.OrderBy(_ => Guid.NewGuid()).Take(count);
        return Result<IEnumerable<MusicInfo>, Empty>.Success(songs);
    }

    private static bool ScoreSingleTerm(string term_clean, MusicInfo r)
    {
        var romanized_title_clean = r.RomanizedTitle is null ? null :
            LevenshteinDistance.RemoveFormatting(ParentesisRegex().Replace(r.RomanizedTitle, string.Empty));

        var original_title_clean = r.OriginalTitle is null ? null :
            LevenshteinDistance.RemoveFormatting(ParentesisRegex().Replace(r.OriginalTitle, string.Empty));

        var romanized_artist_clean = r.RomanizedAuthor is null ? null :
            LevenshteinDistance.RemoveFormatting(r.RomanizedAuthor);

        var original_artist_clean = r.OriginalAuthor is null ? null :
            LevenshteinDistance.RemoveFormatting(r.OriginalAuthor);

        var eval =
                    romanized_title_clean != null &&
                    (LevenshteinDistance.ComputeStrict(romanized_title_clean, term_clean) < 2 ||
                    LevenshteinDistance.ComputeStrict($"{romanized_title_clean}{romanized_artist_clean}", term_clean) < 3 ||
                    LevenshteinDistance.ComputeStrict($"{romanized_artist_clean}{romanized_title_clean}", term_clean) < 3 ||
                    LevenshteinDistance.ComputeStrict($"{romanized_title_clean}{original_artist_clean}", term_clean) < 3)

                    ||

                    original_title_clean != null &&
                   (LevenshteinDistance.ComputeStrict(original_title_clean, term_clean) < 2 ||
                    LevenshteinDistance.ComputeStrict($"{original_title_clean}{original_artist_clean}", term_clean) < 3 ||
                    LevenshteinDistance.ComputeStrict($"{original_artist_clean}{original_title_clean}", term_clean) < 3 ||
                    LevenshteinDistance.ComputeStrict($"{original_title_clean}{romanized_artist_clean}", term_clean) < 3);
        return eval;
    }

    public Result<MusicInfo, Empty> SearchById(string id)
    {
        var search = Songs.AsParallel().FirstOrDefault(r => r.ID == id) ??
                     // Second pass for regenerated infos.
                     Songs.AsParallel().FirstOrDefault(r => (r.ID ?? "  ")[..^2] == id[..^2]);

        return search != null ?
            Result<MusicInfo, Empty>.Success(search) :
            Result<MusicInfo, Empty>.Error(default);
    }

    [GeneratedRegex(@"\(.*?\)")]
    private static partial Regex ParentesisRegex();
}