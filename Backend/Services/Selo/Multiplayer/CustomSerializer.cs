using System.Text.Encodings.Web;
using System.Text.Json;

namespace Selo.Multiplayer;

// ponytail: copied from Gaida.Core.Platforms.CustomSerializer rather than referenced —
// Selo takes no project reference into this repo (see VirtualPlayer.cs / TrackDto.cs), so
// this is duplicated, not shared. Keep the two in sync if the escaping policy ever changes.
public static class CustomSerializer
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    ///     For serializing into a caller-owned <see cref="Utf8JsonWriter" />: that overload takes
    ///     its escaping from the writer, not from <see cref="SerializerOptions" />, so the encoder
    ///     has to be repeated here or the output quietly changes.
    /// </summary>
    public static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}