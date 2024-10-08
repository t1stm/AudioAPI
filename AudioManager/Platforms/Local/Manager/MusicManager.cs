using System.Text;
using System.Text.RegularExpressions;
using Audio.Utils;
using Newtonsoft.Json;
using Result;
using Result.Objects;

namespace AudioManager.Platforms.Local.Manager;

public class MusicManager
{
    protected readonly CoverExtractor CoverExtractor = new();
    protected string Domain = string.Empty;
    protected string StorageDirectory = string.Empty;
    protected string AlbumCoverLocation = string.Empty;
    protected readonly List<MusicInfo> Songs = [];
    
    public void Initialize()
    {
        Domain = Environment.GetEnvironmentVariable("DOMAIN", EnvironmentVariableTarget.Process) ?? string.Empty;
        StorageDirectory = Environment.GetEnvironmentVariable("STORAGE", EnvironmentVariableTarget.Process) ?? "./";
        AlbumCoverLocation = Domain + "/Album_Covers";
        
        Load();
        CoverExtractor.Extract(StorageDirectory);
    }

    protected void Load()
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
        
        foreach (var info in Songs) info.CoverLocation = info.CoverLocation?.Replace("$[DOMAIN]", AlbumCoverLocation);
    }

    private static IEnumerable<MusicInfo> ParseArtistFolder(string artist)
    {
        var artist_name = artist.Split(Path.PathSeparator)[^1];
        var json_file = Path.Combine(artist, $"{artist_name}.json");

        var songs = Directory.GetFiles($"{artist}", "*", SearchOption.TopDirectoryOnly)
            .Where(IsAudioBasedOnFileExtension).ToList();
        
        var serializer = new JsonSerializer
        {
            Formatting = Formatting.Indented,
            StringEscapeHandling = StringEscapeHandling.EscapeHtml
        };
        
        using var file_stream = File.Open(json_file, FileMode.OpenOrCreate, FileAccess.Read, FileShare.None);

        if (File.Exists(json_file))
        {
            using var sr = new StreamReader(file_stream, Encoding.UTF8, true, 4096, true);
            var json = sr.ReadToEnd();
            
            var items = JsonConvert.DeserializeObject<List<MusicInfo>>(json) ?? Enumerable.Empty<MusicInfo>().ToList();
            if (items.Count == songs.Count) return items;

            var update = UpdateData(items, songs);
            file_stream.Seek(0, SeekOrigin.Begin);
            
            using var writer = new StreamWriter(file_stream, Encoding.UTF8);
            serializer.Serialize(writer, update);
            
            return update;
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
        entry.Title ??= Romanize.FromCyrillic(title).Trim();
        entry.Artist ??= romanized_author.Trim();
        entry.RelativeLocation ??= string.Join('/', split[^3..]);
        entry.UpdateRandomId();
        
        return entry;
    }

    protected static bool IsAudioBasedOnFileExtension(string file_name)
    {
        return file_name.EndsWith(".flac") || file_name.EndsWith(".ogg") ||
               file_name.EndsWith(".mp3") || file_name.EndsWith(".wav") || 
               file_name.EndsWith(".mka") || file_name.EndsWith(".adts") ||
               file_name.EndsWith(".wma");
    }
    
    public Result<MusicInfo, Empty> SearchOneByTerm(string term)
    {
        var found = Songs.AsParallel()
            .FirstOrDefault(r => ScoreSingleTerm(term, r));
        
        return found != null ? 
            Result<MusicInfo, Empty>.Success(found) : 
            Result<MusicInfo, Empty>.Error(default);
    }

    private static bool ScoreSingleTerm(string term, MusicInfo r)
    {
        return LevenshteinDistance.ComputeLean(r.Title, term) < 2 ||
               LevenshteinDistance.ComputeLean($"{r.Title} - {r.Artist}", term) < 3 ||
               LevenshteinDistance.ComputeLean($"{r.Title} {r.Artist}", term) < 3 ||
               LevenshteinDistance.ComputeLean($"{r.Artist} {r.Title}", term) < 3 ||
               LevenshteinDistance.ComputeLean($"{r.Artist} - {r.Title}", term) < 3 ||
               LevenshteinDistance.ComputeLean(r.OriginalTitle, term) < 2 ||
               LevenshteinDistance.ComputeLean($"{r.OriginalTitle} - {r.OriginalAuthor}", term) < 3 ||
               LevenshteinDistance.ComputeLean($"{r.OriginalTitle} {r.OriginalAuthor}", term) < 3 ||
               LevenshteinDistance.ComputeLean($"{r.OriginalAuthor} {r.OriginalTitle}", term) < 3 ||
               LevenshteinDistance.ComputeLean($"{r.OriginalAuthor} - {r.OriginalTitle}", term) < 3 ||
               LevenshteinDistance.ComputeLean($"{r.OriginalTitle} - {r.Artist}", term) < 3 ||
               LevenshteinDistance.ComputeLean($"{r.Title} - {r.OriginalAuthor}", term) < 3;
    }
    
    public IEnumerable<MusicInfo> OrderByTerm(string term)
    {
        if (string.IsNullOrEmpty(term)) return GetAll();

        var ordered = from r in Songs.AsParallel()
            orderby
                Min(
                    // Romanized data pass.
                    LevenshteinDistance.ComputeLean(r.Title, term),
                    LevenshteinDistance.ComputeLean($"{r.Title} - {r.Artist}", term),
                    LevenshteinDistance.ComputeLean($"{r.Title} {r.Artist}", term),
                    LevenshteinDistance.ComputeLean($"{r.Artist} {r.Title}", term),
                    LevenshteinDistance.ComputeLean($"{r.Artist} - {r.Title}", term),
                    // Original data pass.
                    LevenshteinDistance.ComputeLean(r.OriginalTitle, term),
                    LevenshteinDistance.ComputeLean($"{r.OriginalTitle} - {r.OriginalAuthor}", term),
                    LevenshteinDistance.ComputeLean($"{r.OriginalTitle} {r.OriginalAuthor}", term),
                    LevenshteinDistance.ComputeLean($"{r.OriginalAuthor} {r.OriginalTitle}", term),
                    LevenshteinDistance.ComputeLean($"{r.OriginalAuthor} - {r.OriginalTitle}", term),
                    // Added step to help me find songs. I hope this doesn't break anything.
                    LevenshteinDistance.ComputeLean(r.Artist, term),
                    LevenshteinDistance.ComputeLean(r.OriginalAuthor, term))
            select r;

        return ordered;
    }

    private static int Min(params int[] values)
    {
        return values.Min();
    }
    
    public IEnumerable<MusicInfo> SearchByPattern(string search)
    {
        try
        {
            var pattern = search;
            if (pattern.Length == 0) return [];
            pattern = pattern.Replace("*", ".*");
            return Songs.AsParallel().Where(r => Regex.IsMatch(r.RelativeLocation ?? "", $"^{pattern}"));
        }
        catch (Exception)
        {
            return [];
        }
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

    public IEnumerable<MusicInfo> GetAll()
    {
        var copy = Songs.ToArray();
        return copy;
    }
}