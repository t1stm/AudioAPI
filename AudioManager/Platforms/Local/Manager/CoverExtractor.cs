using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace AudioManager.Platforms.Local.Manager;

public class CoverExtractor
{
    public string ExportLocation = "./Album_Covers";
    
    public void Extract(string location)
    {
        ExportLocation = Environment.GetEnvironmentVariable("ALBUM_COVERS", EnvironmentVariableTarget.Process) ?? ExportLocation;
        
        if (!Directory.Exists(ExportLocation)) Directory.CreateDirectory(ExportLocation);
        foreach (var genre_directory in Directory.GetDirectories(location))
        foreach (var artist_directory in Directory.GetDirectories(genre_directory))
            ParseFolder(artist_directory);
    }
    
    public void ParseFolder(string folder)
    {
        var serializer = new JsonSerializer
        {
            Formatting = Formatting.Indented,
            StringEscapeHandling = StringEscapeHandling.EscapeHtml
        };
        
        using var file_stream = File.Open($"{folder}/Info.json", FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.ReadWrite);

        using var reader = new StreamReader(file_stream, Encoding.UTF8, true, 1024, true);

        var json = reader.ReadToEnd();
        var items = JsonConvert.DeserializeObject<List<MusicInfo>>(json) ?? [];

        foreach (var info in items)
        {
            var location = info.ToMusicResult([]).Path;
            var image = Flac.GetImageFromFile(location);

            if (!image.HasData)
            {
                image = Id3v2.GetImageFromTag(location);
                if (!image.HasData) continue;
            }

            var hash = Sha1Generator.Get(image.Data!);
            var extension = Flac.GetImageFiletype(image.Data!);

            var filename = $"{ExportLocation}/{hash}.{extension}";
            info.CoverLocation = $"$[DOMAIN]/{hash}.{extension}";
            if (File.Exists(filename)) continue;
            File.WriteAllBytes(filename, image.Data!);
        }

        file_stream.Position = 0;

        using var writer = new StreamWriter(file_stream, Encoding.UTF8);
        serializer.Serialize(writer, items);
    }
}

public static class Sha1Generator
{
    public static string Get(byte[] sourceData)
    {
        var hash = SHA1.HashData(sourceData);
        var hashString = BitConverter.ToString(hash);
        return hashString.Replace("-", string.Empty).ToLower();
    }
}