using System.Text.Json.Serialization;
using Audio.Utils;

namespace AudioManager.Platforms.Local;

public class MusicInfo
{
    [JsonInclude]
    public string? ID { get; set; }
    [JsonInclude]
    public string? Title { get; set; }
    [JsonInclude]
    public string? Artist { get; set; }
    [JsonInclude]
    public string? Album { get; set; }
    [JsonInclude]
    public TimeSpan Duration { get; set; }
    [JsonInclude]
    public string? CoverLocation { get; set; }
    [JsonInclude]
    public string? RelativeLocation { get; set; }
    
    [JsonInclude] [JsonPropertyName("authorOriginal")]
    public string? OriginalAuthor { get; set; }

    [JsonInclude] [JsonPropertyName("titleOriginal")]
    public string? OriginalTitle {get; set;}

    public MusicResult ToMusicResult(IReadOnlyList<ContentGetter> getters)
    {
        return new MusicResult
        {
            ID = ID ??= UpdateRandomId(),
            Downloaders = getters,
            Name = Title,
            Artist = Artist,
            Album = Album,
            Duration = Duration,
            Path = RelativeLocation ?? string.Empty,
            ThumbnailUrl = CoverLocation
        };
    }

    public string UpdateRandomId()
    {
        var artist_part = (Artist?.Length > 2 ? Artist?[..2] : Artist)?.ToLower();
        var title_part = (Title?.Length > 6 ? Title?[..6] : Title + new string('0', 6 - Title?.Length ?? 0))?.ToLower()
            .Replace(' ', '-');
        return $"{artist_part}{title_part}-{Generation.RandomString(2)}";
    }
}