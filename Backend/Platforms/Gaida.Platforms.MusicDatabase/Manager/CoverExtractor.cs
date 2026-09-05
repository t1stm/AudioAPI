using System.Security.Cryptography;
using System.Text.Json;

namespace Gaida.Platforms.MusicDatabase.Manager;

public class CoverExtractor
{
    private static readonly Lock ExportLock = new();
    public string ExportLocation = "./Album_Covers";

    public void Extract(string location)
    {
        ExportLocation = Environment.GetEnvironmentVariable("ALBUM_COVERS", EnvironmentVariableTarget.Process) ??
                         ExportLocation;

        Directory.CreateDirectory(ExportLocation);
        Parallel.ForEach(
            Directory.GetDirectories(location, "*", SearchOption.AllDirectories)
                .Where(folder => File.Exists($"{folder}/Info.json")),
            ParseFolder);
    }

    public void ParseFolder(string folder)
    {
        using var fileStream = File.Open($"{folder}/Info.json", FileMode.Open, FileAccess.ReadWrite,
            FileShare.ReadWrite);

        if (fileStream.Length == 0) return;

        var items = JsonSerializer.Deserialize<List<MusicInfo>>(fileStream, MusicInfo.SerializerOptions) ?? [];
        var change = false;

        foreach (var info in items.Where(m => string.IsNullOrWhiteSpace(m.CoverUrl)))
        {
            var location = info.ToMusicResult([]).Path;
            // A file that has been deleted since the last scan is not an error worth a crash: Flac and
            // WavPack answer null for a missing path, but Id3V2 throws, and that took the whole library
            // load down with it.
            if (!File.Exists(location)) continue;

            var image = Flac.GetImageFromFile(location) ?? WavPack.GetImageFromFile(location) ??
                Id3V2.GetImageFromTag(location);
            if (image is null) continue;

            var hash = Convert.ToHexStringLower(SHA1.HashData(image));
            var extension = Flac.GetImageFiletype(image);

            var filename = $"{ExportLocation}/{hash}.{extension}";
            info.CoverUrl = $"$[DOMAIN]/{hash}.{extension}";
            change = true;

            // ponytail: one lock for every cover write; they are rare and small, split it per-hash if that ever shows up.
            lock (ExportLock)
            {
                if (!File.Exists(filename)) File.WriteAllBytes(filename, image);
            }
        }

        if (!change) return;

        fileStream.SetLength(0);
        fileStream.Position = 0;
        JsonSerializer.Serialize(fileStream, items, MusicInfo.SerializerOptions);
    }
}