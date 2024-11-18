using System.Text;
using System.Text.RegularExpressions;
using Audio.Utils;
using Newtonsoft.Json;
using Result;
using Result.Objects;

namespace AudioManager.Platforms.Local.Manager;

public partial class MusicManager
{
    public static string Domain => Environment.GetEnvironmentVariable("DOMAIN", EnvironmentVariableTarget.Process) ?? string.Empty;
    public static string StorageDirectory => Environment.GetEnvironmentVariable("STORAGE", EnvironmentVariableTarget.Process) ?? "./";
    public static string AlbumCoverLocation => Domain + "/Album_Covers";
    
    protected readonly CoverExtractor CoverExtractor = new();
    protected readonly List<MusicInfo> Songs = [];
    
    public void Initialize()
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
        
        Load();
        CoverExtractor.Extract(StorageDirectory);
    }

    protected void Load()
    {
        lock (Songs)
        {
            var genres = Directory.EnumerateDirectories(StorageDirectory, "*", SearchOption.TopDirectoryOnly);

            foreach (var genre in genres)
            {
                var artists = Directory.EnumerateDirectories(genre, "*", SearchOption.TopDirectoryOnly);
                foreach (var artist in artists)
                {
                    Songs.AddRange(ParseArtistFolder(artist));
                }
            }

            Songs.ForEach(s => s.CoverUrl = s.CoverUrl?.Replace("$[DOMAIN]", AlbumCoverLocation));
        }
    }

    private static IEnumerable<MusicInfo> ParseArtistFolder(string artist)
    {
        Console.WriteLine($"Loading artist: \'{artist}\'");
        var artist_name = artist.Split(Path.PathSeparator)[^1];
        var json_file = Path.Combine(artist, "Info.json");

        var songs = Directory.GetFiles($"{artist}", "*", SearchOption.TopDirectoryOnly)
            .Where(IsAudioBasedOnFileExtension).ToList();
        
        var serializer = new JsonSerializer
        {
            Formatting = Formatting.Indented,
            StringEscapeHandling = StringEscapeHandling.EscapeHtml
        };
        
        using var file_stream = File.Open(json_file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        if (File.Exists(json_file))
        {
            using var sr = new StreamReader(file_stream, Encoding.UTF8, true, 4096, true);
            var json = sr.ReadToEnd();
            
            try
            {
                var items = JsonConvert.DeserializeObject<List<MusicInfo>>(json) ??
                            Enumerable.Empty<MusicInfo>().ToList();
                if (items.Count == songs.Count) return items;

                var update = UpdateData(items, songs);
                file_stream.SetLength(0);
                file_stream.Flush();
                file_stream.Seek(0, SeekOrigin.Begin);

                using var writer = new StreamWriter(file_stream, Encoding.UTF8);
                serializer.Serialize(writer, update);

                return update;
            }
            catch (Exception e)
            {
                // TODO: log with logger
                Console.WriteLine($"Error thrown for \'{artist}\': \'{e}\'");
            }
        }

        {
            var list = songs.Where(IsAudioBasedOnFileExtension)
                .Select(ParseFile)
                .ToList();
            using var writer = new StreamWriter(file_stream, Encoding.UTF8);
            
            serializer.Serialize(writer, list);
            file_stream.Close();
            
            return list;
        }
    }
    
    private static IEnumerable<MusicInfo> UpdateData(List<MusicInfo> existing, List<string> files)
    {
        foreach (var info in existing) yield return info;

        var new_files = files.Where(location =>
            IsAudioBasedOnFileExtension(location) &&
            existing.All(m =>
            {
                var relative_location = string.Join('/', location.Split('/')[^3..]);
                return m.RelativeLocation != relative_location;
            }));

        foreach (var file in new_files) yield return ParseFile(file);
    }

    private static MusicInfo ParseFile(string location)
    {
        var split = location.Split('/');
        var filename = split[^1];
        var romanized_author = split[^2];

        var filename_split = filename.Split(" - ");
        var author = filename_split[0];
        var title = string.Join('.',
            string.Join('-', filename_split[1..]).Split('.')[..^1]);
        
        var entry = MediaInfo.GetInformation(location).GetAwaiter().GetResult();
        entry.OriginalTitle ??= title.Trim();
        entry.OriginalAuthor ??= author.Trim();
        entry.RomanizedTitle ??= Romanize.FromCyrillic(title).Trim();
        entry.RomanizedAuthor ??= romanized_author.Trim();
        entry.RelativeLocation ??= string.Join('/', split[^3..]);
        entry.ID = entry.UpdateRandomId();
        
        return entry;
    }

    protected static bool IsAudioBasedOnFileExtension(string file_name)
    {
        return file_name.EndsWith(".flac") || file_name.EndsWith(".ogg") ||
               file_name.EndsWith(".mp3") || file_name.EndsWith(".wav") || 
               file_name.EndsWith(".mka") || file_name.EndsWith(".adts") ||
               file_name.EndsWith(".wma") || file_name.EndsWith(".wv");
    }
    
    public Result<IEnumerable<MusicInfo>, Empty> SearchByTerm(string term)
    {
        var found = Songs.Where(r => ScoreSingleTerm(term, r));
        return Result<IEnumerable<MusicInfo>, Empty>.Success(found);
    }

    private static bool ScoreSingleTerm(string term, MusicInfo r)
    {
        var term_clean = ParentesisRegex()
            .Replace(term, string.Empty);
        
        var romanized_clean = r.RomanizedTitle is null ? null : 
            ParentesisRegex().Replace(r.RomanizedTitle, string.Empty);
        
        var original_clean = r.OriginalTitle is null ? null : 
            ParentesisRegex().Replace(r.OriginalTitle, string.Empty);
        
        var eval = 
                    romanized_clean != null && 
                    (LevenshteinDistance.ComputeLean(romanized_clean, term_clean) < 2 ||
                    LevenshteinDistance.ComputeLean($"{romanized_clean}{r.RomanizedAuthor}", term_clean) < 3 ||
                    LevenshteinDistance.ComputeLean($"{r.RomanizedAuthor}{romanized_clean}", term_clean) < 3 ||
                    LevenshteinDistance.ComputeLean($"{romanized_clean}{r.OriginalAuthor}", term_clean) < 3) 
                    
                    || 
                    
                    original_clean != null && 
                   (LevenshteinDistance.ComputeLean(original_clean, term_clean) < 2 ||
                    LevenshteinDistance.ComputeLean($"{original_clean}{r.OriginalAuthor}", term_clean) < 3 ||
                    LevenshteinDistance.ComputeLean($"{r.OriginalAuthor}{original_clean}", term_clean) < 3 ||
               
                    LevenshteinDistance.ComputeLean($"{original_clean}{r.RomanizedAuthor}", term_clean) < 3);
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