using System.Text.Json.Serialization;
using Audio.Utils;
using AudioManager.Platforms.Local.Manager;

namespace AudioManager.Platforms.Local;

public class MusicInfo
{
    [JsonInclude] [JsonPropertyName("id")] 
    public string? ID { get; set; }
    
    [JsonInclude] [JsonPropertyName("titleRomanized")]
    public string? RomanizedTitle { get; set; }
    
    [JsonInclude] [JsonPropertyName("authorRomanized")]
    public string? RomanizedAuthor { get; set; }
    
    [JsonInclude] 
    public string? Album { get; set; }
    
    [JsonInclude]
    public TimeSpan Duration { get; set; }
    
    [JsonInclude] [JsonPropertyName("coverUrl")]
    public string? CoverLocation { get; set; }
    
    [JsonInclude] [JsonPropertyName("location")]
    public string? RelativeLocation { get; set; }
    
    [JsonInclude] [JsonPropertyName("authorOriginal")]
    public string? OriginalAuthor { get; set; }

    [JsonInclude] [JsonPropertyName("titleOriginal")]
    public string? OriginalTitle { get; set; }
    
    [JsonInclude] [JsonPropertyName("length")]
    public double Length { get => Duration.TotalMilliseconds; set => Duration = TimeSpan.FromMilliseconds(value); }
    
    [JsonInclude] [JsonPropertyName("romanizedGuestArtists")]
    public string[]? RomanizedGuestArtists;
    
    [JsonInclude] [JsonPropertyName("romanizedGuestArtists")]
    public string[]? OriginalGuestArtists;
    
    [JsonInclude] [JsonPropertyName("romanizedGuestArtists")]
    public string[]? RomanizedOtherTitles;
    
    [JsonInclude] [JsonPropertyName("romanizedGuestArtists")]
    public string[]? OriginalOtherTitles;

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
            ThumbnailUrl = CoverLocation,
            OriginalTitle = OriginalTitle,
            OriginalArtist = OriginalAuthor 
        };
    }

    public string UpdateRandomId()
    {
        var artist_part = (RomanizedAuthor?.Length > 2 ? RomanizedAuthor?[..2] : RomanizedAuthor)?.ToLower();
        var title_part = (RomanizedTitle?.Length > 6 ? RomanizedTitle?[..6] : RomanizedTitle + new string('0', 6 - RomanizedTitle?.Length ?? 0))?.ToLower()
            .Replace(' ', '-');
        return $"{artist_part}{title_part}-{Generation.RandomString(2)}";
    }
}