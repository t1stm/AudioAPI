using TagLib;
using File = System.IO.File;

namespace Gaida.Platforms.MusicDatabase.Manager;

public static class WavPack
{
    /// <returns>The embedded cover, or <c>null</c> when the file has none.</returns>
    public static byte[]? GetImageFromFile(string location)
    {
        if (!File.Exists(location) || !location.EndsWith(".wv")) return null;

        // ponytail: taglib is already referenced and reads the APEv2 "Cover Art (Front)" item, so no wvunpack subprocess.
        var pictures = TagLib.File.Create(location).GetTag(TagTypes.Ape)?.Pictures;
        return pictures is null || pictures.Length < 1 ? null : pictures[0].Data.Data;
    }
}