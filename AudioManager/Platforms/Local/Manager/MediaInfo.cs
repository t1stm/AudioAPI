using System.Diagnostics;
using System.Text.Json;

namespace AudioManager.Platforms.Local.Manager;

public static class MediaInfo
{
    public static async Task<MusicInfo> GetInformation(string location)
    {
        var music_info = new MusicInfo
        {
            ID = string.Empty
        };
        var processed_location = location.Replace("\"", "\\\"");
        
        var program = Process.Start(new ProcessStartInfo
        {
            FileName = "mediainfo",
            Arguments = $"--Output=JSON \"{processed_location}\"",
            RedirectStandardOutput = true
        });
        if (program == null) return music_info;
        
        await program.WaitForExitAsync();
        var json = await JsonDocument.ParseAsync(program.StandardOutput.BaseStream);
        
        if (!json.RootElement.TryGetProperty("media", out var media)) return music_info;
        if (!media.TryGetProperty("track", out var info_array)) return music_info;
        
        var is_audio = info_array.GetArrayLength() == 2;
        var general = is_audio ? info_array[1] : info_array[0];
        
        if (is_audio)
        {
            if (general.TryGetProperty("Title", out var title))
                music_info.OriginalTitle = title.GetString();
            if (general.TryGetProperty("Performer", out var author))
                music_info.OriginalAuthor = author.GetString();
        }

        if (!general.TryGetProperty("Duration", out var duration)) return music_info;
        if (double.TryParse(duration.GetString(), out var length_seconds))
            music_info.Duration = TimeSpan.FromSeconds(length_seconds);
        
        return music_info;
    }
}