using Gaida.Core.Streams;

namespace Dunav;

/// <summary>
///     One cached response: the upstream body (still filling, or finished) plus the headers it
///     arrived with. Storing headers alongside the bytes matters -- a cache hit replayed with a default
///     content type breaks playback in browsers.
/// </summary>
public class CacheEntry
{
    public required StreamSpreader Spreader { get; init; }
    public string ContentType { get; set; } = "application/octet-stream";
    public string? ContentDisposition { get; set; }
    public string? ETag { get; set; }
}