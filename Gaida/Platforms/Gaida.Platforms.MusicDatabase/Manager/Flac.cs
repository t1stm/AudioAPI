using System.Diagnostics;
using File = System.IO.File;

namespace Gaida.Platforms.MusicDatabase.Manager;

public static class Flac
{
    /// <returns>The embedded cover, or <c>null</c> when the file has none.</returns>
    public static byte[]? GetImageFromFile(string location)
    {
        if (!File.Exists(location) || !location.Contains(".flac")) return null;

        var process = Process.Start(new ProcessStartInfo
        {
            Arguments = $"--export-picture-to=- \"{location}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            FileName = "metaflac"
        });
        if (process == null) return null;

        var memoryStream = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(memoryStream);

        return memoryStream.Length < 1 ? null : memoryStream.ToArray();
    }

    public static string GetImageFiletype(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> pngHeader = [137, 80, 78, 71, 13, 10, 26, 10];
        ReadOnlySpan<byte> jpegHeader = [255, 216, 255];

        if (data.StartsWith(pngHeader)) return "png";
        return data.StartsWith(jpegHeader) ? "jpg" : "";
    }
}
