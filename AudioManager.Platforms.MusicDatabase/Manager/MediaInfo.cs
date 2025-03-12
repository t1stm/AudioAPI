using System.Diagnostics;
using System.Text.Json;
using Audio.Utils;

namespace AudioManager.Platforms.MusicDatabase.Manager;

public static class MediaInfo
{
    public static async Task<MusicInfo> GetInformation(string location)
    {
        var music_info = new MusicInfo
        {
            ID = string.Empty
        };
        var processed_location = location.Replace("\"", "\\\"");

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v quiet -of json -show_entries format \"{processed_location}\"",
            RedirectStandardOutput = true,
        });
    
        if (process == null) return music_info;
        await process.WaitForExitAsync();
    
        try
        {
            var json = await JsonDocument.ParseAsync(process.StandardOutput.BaseStream);
            if (!json.RootElement.TryGetProperty("format", out var format)) return music_info;
        
            if (format.TryGetProperty("duration", out var durationStr) &&
                double.TryParse(durationStr.GetString(), out var length))
            {
                music_info.Length = (ulong)(length * 1000);
            }
            
            if (!format.TryGetProperty("tags", out var tags)) return music_info;
            
            if (tags.TryGetProperty("title", out var title))
                music_info.OriginalTitle = title.GetString();
            
            if (tags.TryGetProperty("artist", out var artist))
                music_info.OriginalAuthor = artist.GetString();

            music_info.RomanizedTitle = Romanize.FromCyrillic(music_info.OriginalTitle ?? "");
            music_info.RomanizedAuthor = Romanize.FromCyrillic(music_info.OriginalAuthor ?? "");
            return music_info;
        }
        catch (Exception)
        {
            // Handle error silently
        }
    
        return music_info;
    }
}