using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gaida.Core.Platforms;
using Gaida.Core.Utils;
using Gaida.Platforms.MusicDatabase.Manager;

namespace Gaida.Platforms.MusicDatabase;

/// <summary>Every string a song can be found by, cleaned once and cached on the entry.</summary>
public sealed record SearchVariants(string[] Titles, string[] Artists, IReadOnlySet<string> Tags);

public class MusicInfo : IJsonOnDeserialized
{
    /// <summary>Layout of the per-artist Info.json files on disk. Property names are the on-disk names.</summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string?[] _legacy = new string?[4];
    private List<string> _artists = [];
    private SearchVariants? _search;
    private List<string> _titles = [];

    public string? ID { get; set; }

    /// <summary>
    ///     Every reading of the name, original first. Later entries are alternates — a romanization when the
    ///     original is not Latin, the filename's or folder's spelling when it disagrees with the tag. Nothing
    ///     downstream picks a winner; matching compares them all.
    /// </summary>
    public List<string> Titles
    {
        get => _titles;
        set
        {
            _titles = value;
            _search = null;
        }
    }

    /// <summary>
    ///     As <see cref="Titles" />. Compound names stay joined here; <see cref="TitleNormalizer.SplitArtists" /> splits
    ///     at match time.
    /// </summary>
    public List<string> Artists
    {
        get => _artists;
        set
        {
            _artists = value;
            _search = null;
        }
    }

    public string? Album { get; set; }
    public string? CoverUrl { get; set; }
    public string? RelativeLocation { get; set; }

    // ponytail: read-only shim for the four-field format. Setter-only properties are never serialized by
    // System.Text.Json, so nothing writes these names back. Delete once no Info.json still carries them.
    [JsonPropertyName("OriginalTitle")]
    public string? LegacyOriginalTitle
    {
        set => _legacy[0] = value;
    }

    [JsonPropertyName("RomanizedTitle")]
    public string? LegacyRomanizedTitle
    {
        set => _legacy[1] = value;
    }

    [JsonPropertyName("OriginalAuthor")]
    public string? LegacyOriginalAuthor
    {
        set => _legacy[2] = value;
    }

    [JsonPropertyName("RomanizedAuthor")]
    public string? LegacyRomanizedAuthor
    {
        set => _legacy[3] = value;
    }

    /// <summary>Set when the entry was read in the four-field format, so the loader knows to re-read its tags.</summary>
    [JsonIgnore]
    public bool WasLegacy { get; private set; }

    [JsonIgnore] public TimeSpan Duration { get; set; }

    /// <summary>The name as tagged: the original.</summary>
    [JsonIgnore]
    public string? Title => Titles.FirstOrDefault();

    [JsonIgnore] public string? Artist => Artists.FirstOrDefault();

    /// <summary>What to show someone who cannot read the original script.</summary>
    [JsonIgnore]
    public string? DisplayTitle => Titles.FirstOrDefault(IsLatin) ?? Title;

    [JsonIgnore] public string? DisplayArtist => Artists.FirstOrDefault(IsLatin) ?? Artist;

    /// <summary>
    ///     Derived, never stored: a flag written at import time would be wrong wherever the transliteration
    ///     failed, and this cannot drift from the array it describes.
    /// </summary>
    [JsonIgnore]
    public bool ContainsRomanized => Titles.Count > 1 && !IsLatin(Titles[0]) && IsLatin(Titles[1]);

    [JsonIgnore] public SearchVariants Search => _search ??= BuildSearch();

    public double Length
    {
        get => Duration.TotalMilliseconds;
        set => Duration = TimeSpan.FromMilliseconds(value);
    }

    public void OnDeserialized()
    {
        // Original first regardless of the order the properties appear in the file.
        if (Titles.Count == 0 && Variants(_legacy[0], _legacy[1]) is { Count: > 0 } titles)
        {
            Titles = titles;
            WasLegacy = true;
        }

        if (Artists.Count == 0 && Variants(_legacy[2], _legacy[3]) is { Count: > 0 } artists)
        {
            Artists = artists;
            WasLegacy = true;
        }
    }

    /// <summary>Trims, romanizes what it can, drops blanks and duplicates. Order is preserved: the first value wins index 0.</summary>
    public static List<string> Variants(params string?[] values)
    {
        var result = new List<string>();
        foreach (var value in values)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            Add(result, trimmed);
            Add(result, Romanize.FromCyrillic(trimmed).Trim());
        }

        return result;
    }

    /// <summary>Appends the path-derived names behind whatever the tags already gave.</summary>
    public void AddNames(string? title, string? artist, string? folder)
    {
        Titles = Merge(Titles, title);
        Artists = Merge(Artists, artist, folder);
    }

    /// <summary>The tag reading, ahead of the names an older scan parsed out of the path.</summary>
    public void PreferTags(MusicInfo tagged)
    {
        Titles = Merge(tagged.Titles, [.. Titles]);
        Artists = Merge(tagged.Artists, [.. Artists]);
    }

    private static List<string> Merge(List<string> head, params string?[] tail)
    {
        var merged = new List<string>();
        foreach (var value in head) Add(merged, value);
        foreach (var value in Variants(tail)) Add(merged, value);

        return merged;
    }

    private static void Add(List<string> into, string value)
    {
        if (value.Length > 0 && !into.Contains(value, StringComparer.OrdinalIgnoreCase)) into.Add(value);
    }

    /// <summary>Latin Extended and its diacritics; everything above is Cyrillic, Greek, CJK or kana.</summary>
    private static bool IsLatin(string value)
    {
        return !value.Any(character => character > 'ͯ');
    }

    private SearchVariants BuildSearch()
    {
        var tags = new HashSet<string>(StringComparer.Ordinal);
        var titles = new List<string>();
        var artists = new List<string>();

        foreach (var title in Titles)
        {
            var (text, titleTags) = TitleNormalizer.NormalizeLibrary(title);
            tags.UnionWith(titleTags);
            Add(titles, LevenshteinDistance.RemoveFormatting(text) ?? string.Empty);
        }

        foreach (var name in Artists.SelectMany(TitleNormalizer.SplitArtists))
            Add(artists, LevenshteinDistance.RemoveFormatting(name) ?? string.Empty);

        return new SearchVariants([.. titles], [.. artists], tags);
    }

    public MusicResult ToMusicResult(IReadOnlyList<ContentGetter> getters)
    {
        return new MusicResult
        {
            ID = "audio://" + (ID ??= UpdateRandomId()),
            Downloaders = getters,
            Name = DisplayTitle,
            Artist = DisplayArtist,
            Album = Album,
            Duration = Duration,
            Path = MusicManager.StorageDirectory + "/" + RelativeLocation,
            ThumbnailUrl = CoverUrl,
            OriginalTitle = Title,
            OriginalArtist = Artist
        };
    }

    public string UpdateRandomId()
    {
        return $"{Prefix(Artists, 2)}{Prefix(Titles, 6)}-{Generation.RandomString(2)}";
    }

    /// <summary>IDs travel in URLs, so the prefix comes from a Latin variant — random when the song has none.</summary>
    private static string Prefix(List<string> variants, int length)
    {
        // A wholly Latin variant first: a mixed one like "До Вчера = Until Yesterday" cleans down to "---unt".
        var source = variants.FirstOrDefault(IsLatin)
                     ?? variants.FirstOrDefault(variant => variant.Any(char.IsAsciiLetterOrDigit))
                     ?? string.Empty;
        var clean = new string(source.Select(character => character == ' ' ? '-' : character)
            .Where(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            .ToArray()).ToLower();

        return clean.Length >= length
            ? clean[..length]
            : clean + Generation.RandomString(length - clean.Length).ToLower();
    }
}