using System.Diagnostics;
using System.Text.Json;

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

        // Drain the pipe before waiting on the process, never after: a 64KB buffer against ffprobe's 80KB
        // of output (one .mp3 here carries a 65KB TRAKTOR4 tag) deadlocks the child on write and this on exit.
        JsonDocument json;
        try
        {
            json = await JsonDocument.ParseAsync(process.StandardOutput.BaseStream);
        }
        catch (JsonException)
        {
            return musicInfo;
        }
        finally
        {
            await process.WaitForExitAsync();
        }

        if (!json.RootElement.TryGetProperty("format", out var format)) return musicInfo;

        if (format.TryGetProperty("duration", out var durationString) &&
            double.TryParse(durationString.GetString(), out var length))
            musicInfo.Length = (ulong)(length * 1000);

        if (!format.TryGetProperty("tags", out var tags)) return musicInfo;

        musicInfo.Titles = MusicInfo.Variants(Tag(tags, "TITLE"));
        musicInfo.Artists = MusicInfo.Variants(Tag(tags, "ARTISTS"), Tag(tags, "ARTIST"));

        return musicInfo;
    }

    /// <summary>
    ///     ffprobe lowercases ID3v2 keys and passes Vorbis comments and APEv2 through verbatim, so the same
    ///     tag arrives as <c>artist</c> from an .mp3 and <c>ARTIST</c> from a .wv. Matching case-sensitively
    ///     read the tags of one file in six.
    /// </summary>
    private static string? Tag(JsonElement tags, params string[] names)
    {
        foreach (var name in names)
        foreach (var tag in tags.EnumerateObject())
            if (string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase))
                return tag.Value.GetString();

        return null;
    }
}