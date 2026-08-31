using TagLib;
using File = TagLib.File;

namespace Gaida.Platforms.MusicDatabase.Manager;

public static class Id3V2
{
    /// <returns>The embedded cover, or <c>null</c> when the tag has none.</returns>
    public static byte[]? GetImageFromTag(string location)
    {
        var file = File.Create(location);
        var tag = file.GetTag(TagTypes.Id3v2);

        var pictures = tag?.Pictures;
        return pictures is null || pictures.Length < 1 ? null : pictures[0].Data.Data;
    }
}
