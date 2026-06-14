using System.Diagnostics;
using System.Text.Json;
using Gaida.Core.Utils;

namespace Gaida.Platforms.MusicDatabase.Manager;

public static class MediaInfo
{
    public static async Task<MusicInfo> GetInformation(string location)
    {
        var musicInfo = new MusicInfo
        {
            ID = string.Empty
        };
        var processedLocation = location.Replace("\"", "\\\"");

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v quiet -of json -show_entries format \"{processedLocation}\"",
            RedirectStandardOutput = true
        });

        if (process == null) return musicInfo;
        await process.WaitForExitAsync();

        JsonDocument json;
        try
        {
            json = await JsonDocument.ParseAsync(process.StandardOutput.BaseStream);
        }
        catch (JsonException)
        {
            return musicInfo;
        }

        if (!json.RootElement.TryGetProperty("format", out var format)) return musicInfo;

        if (format.TryGetProperty("duration", out var durationString) &&
            double.TryParse(durationString.GetString(), out var length))
            musicInfo.Length = (ulong)(length * 1000);

        if (!format.TryGetProperty("tags", out var tags)) return musicInfo;

        if (tags.TryGetProperty("title", out var title))
        {
            musicInfo.OriginalTitle = title.GetString();
            musicInfo.RomanizedTitle = Romanize.FromCyrillic(musicInfo.OriginalTitle);
        }

        if (!tags.TryGetProperty("artist", out var artist)) return musicInfo;

        musicInfo.OriginalAuthor = artist.GetString();
        musicInfo.RomanizedAuthor = Romanize.FromCyrillic(musicInfo.OriginalAuthor ?? "");

        return musicInfo;
    }
}