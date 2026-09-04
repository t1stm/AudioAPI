using Gaida.Core.Streams;

namespace Dunav;

/// <summary>
///     One cached response: the body, plus the headers it arrived with. Storing headers alongside the bytes
///     matters -- a cache hit replayed with a default content type breaks playback in browsers.
/// </summary>
/// <remarks>
///     The body is a <see cref="StreamSpreader" />, the same one-writer/many-readers-over-a-file primitive the
///     platform pods use. Dunav owns only the expiry and eviction policy around it.
/// </remarks>
public class CacheEntry
{
    public required StreamSpreader Body { get; init; }

    public string ContentType { get; set; } = "application/octet-stream";
    public string? ContentDisposition { get; set; }
    public string? ETag { get; set; }
}
