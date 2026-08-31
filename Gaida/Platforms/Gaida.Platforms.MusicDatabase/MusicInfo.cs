using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gaida.Core.Platforms;
using Gaida.Core.Utils;
using Gaida.Platforms.MusicDatabase.Manager;

namespace Gaida.Platforms.MusicDatabase;

public class MusicInfo
{
    /// <summary>Layout of the per-artist Info.json files on disk. Property names are the on-disk names.</summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public string? ID { get; set; }
    public string? RomanizedTitle { get; set; }
    public string? RomanizedAuthor { get; set; }
    public string? OriginalAuthor { get; set; }
    public string? OriginalTitle { get; set; }
    public string? Album { get; set; }
    public string? CoverUrl { get; set; }
    public string? RelativeLocation { get; set; }

    [JsonIgnore] public TimeSpan Duration { get; set; }

    public double Length
    {
        get => Duration.TotalMilliseconds;
        set => Duration = TimeSpan.FromMilliseconds(value);
    }

    public MusicResult ToMusicResult(IReadOnlyList<ContentGetter> getters)
    {
        return new MusicResult
        {
            ID = "audio://" + (ID ??= UpdateRandomId()),
            Downloaders = getters,
            Name = RomanizedTitle,
            Artist = RomanizedAuthor,
            Album = Album,
            Duration = Duration,
            Path = MusicManager.StorageDirectory + "/" + RelativeLocation,
            ThumbnailUrl = CoverUrl,
            OriginalTitle = OriginalTitle,
            OriginalArtist = OriginalAuthor
        };
    }

    public string UpdateRandomId()
    {
        var artistPart = (RomanizedAuthor?.Length > 2 ? RomanizedAuthor?[..2] : RomanizedAuthor)?.ToLower();
        var titlePart =
            (RomanizedTitle?.Length > 6
                ? RomanizedTitle?[..6]
                : RomanizedTitle + new string('0', 6 - RomanizedTitle?.Length ?? 0))?.ToLower()
            .Replace(' ', '-');
        return $"{artistPart}{titlePart}-{Generation.RandomString(2)}";
    }
}
