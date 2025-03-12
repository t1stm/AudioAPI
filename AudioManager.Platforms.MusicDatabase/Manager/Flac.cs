using System.Diagnostics;
using AudioManager.Platforms.MusicDatabase.Manager.Objects;
using File = System.IO.File;

namespace AudioManager.Platforms.MusicDatabase.Manager;

public static class Flac
{
    public static EmbeddedImage GetImageFromFile(string location)
    {
        if (!File.Exists(location) || !location.Contains(".flac"))
            return new EmbeddedImage
            {
                HasData = false
            };

        var process = Process.Start(new ProcessStartInfo
        {
            Arguments = $"--export-picture-to=- \"{location}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            FileName = "metaflac"
        });
        if (process == null)
            return new EmbeddedImage
            {
                HasData = false
            };
        process.Start();

        var memoryStream = new MemoryStream();
        var data = process.StandardOutput.BaseStream;
        data.CopyTo(memoryStream);
        memoryStream.Position = 0;
        var array = memoryStream.ToArray();

        if (array.Length < 1)
            return new EmbeddedImage
            {
                HasData = false
            };

        return new EmbeddedImage
        {
            HasData = true,
            Data = array,
            MimeType = ""
        };
    }

    public static string GetImageFiletype(byte[] data)
    {
        Span<byte> PngHeader = [137, 80, 78, 71, 13, 10, 26, 10];
        Span<byte> JpegHeader = [255, 216, 255];
        return data.Length switch
        {
            > 9 when HasHeader(PngHeader, data) => "png",
            > 5 when HasHeader(JpegHeader, data) => "jpg",
            _ => ""
        };
    }

    private static bool HasHeader(Span<byte> header, IReadOnlyList<byte> source)
    {
        if (header.Length > source.Count) return false;

        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] != source[i]) return false;
        }

        return true;
    }
}