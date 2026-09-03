using System.Collections.Concurrent;
using Gaida.Core.Streams;
using Microsoft.AspNetCore.Mvc;

namespace Dunav.Controllers;

/// <summary>
///     Public route shape kept identical to Gaida.API's <c>Content.cs</c>: same three paths, same query
///     params. Downstream of it every request is coalesced through <see cref="CacheService" /> instead of
///     hitting Gaida.API directly.
/// </summary>
[ApiController]
public class AudioController(ILogger<AudioController> logger, CacheService cache) : ControllerBase
{
    [HttpGet]
    [Route("/Audio/DownloadRaw")]
    public async Task<IActionResult> DownloadRaw(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return NotFound();

        var key = CacheService.RawKey(id);
        var entry = await GetOrFetch(key, $"/Audio/DownloadRaw?id={Uri.EscapeDataString(id)}", out _);
        if (entry is null) return StatusCode(502);

        return await Respond(entry);
    }

    [HttpGet]
    [Route("/Audio/Download/{codec:required}/{bitrate:int:required}")]
    public async Task<IActionResult> Download(string codec, int bitrate, string id)
    {
        if (bitrate < 8) return BadRequest("Bitrate must be greater than 8");
        if (string.IsNullOrWhiteSpace(id)) return NotFound("No ID provided");

        var key = CacheService.EncodedKey(codec, bitrate, id);
        var entry = await GetOrFetch(key, UpstreamDownloadPath(codec, bitrate, id), out _);
        if (entry is null) return StatusCode(502);

        return await Respond(entry);
    }

    /// <summary>
    ///     Starts the same upstream fetch <see cref="Download" /> would, without a body: the next Download for
    ///     the same codec/bitrate/id then finds it already running or cached.
    /// </summary>
    [HttpGet]
    [Route("/Audio/Preload/{codec:required}/{bitrate:int:required}")]
    public async Task<IActionResult> Preload(string codec, int bitrate, string id)
    {
        if (bitrate < 8) return BadRequest("Bitrate must be greater than 8");
        if (string.IsNullOrWhiteSpace(id)) return NotFound("No ID provided");

        var key = CacheService.EncodedKey(codec, bitrate, id);

        // 200 for every caller after the first: the fetch is already running or finished, and the lookup has
        // just pushed its expiry back, which is the whole of what a repeat preload can usefully do.
        if (cache.TryGet(key, out _)) return Ok();

        logger.LogInformation("Preloading '{Id}' {Codec} {Bitrate}", id, codec, bitrate);
        var entry = await GetOrFetch(key, UpstreamDownloadPath(codec, bitrate, id), out var started);
        if (entry is null) return StatusCode(502);

        // Two callers can reach this together and both find TryGet empty; only one of them added the
        // entry, and only that one gets the 202.
        return started ? Accepted() : Ok();
    }

    private static string UpstreamDownloadPath(string codec, int bitrate, string id)
    {
        return $"/Audio/Download/{codec}/{bitrate}?id={Uri.EscapeDataString(id)}";
    }

    private Task<CacheEntry?> GetOrFetch(string key, string upstreamPath, out bool started)
    {
        return cache.GetOrStartAsync(key, entry => cache.FetchAsync(entry, upstreamPath, CancellationToken.None),
            out started);
    }

    private async Task<IActionResult> Respond(CacheEntry entry)
    {
        Response.ContentType = entry.ContentType;
        if (entry.ContentDisposition is not null)
            Response.Headers.Append("Content-Disposition", entry.ContentDisposition);
        if (entry.ETag is not null) Response.Headers.ETag = entry.ETag;
        Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");

        // Only claim range support for a finished body: promising it mid-fetch makes players seek into a 200.
        Response.Headers.AcceptRanges = entry.Spreader.Closed ? "bytes" : "none";

        if (entry.Spreader.Closed && Request.Headers.Range.Count > 0)
            return await BufferedRangeResponse(entry.Spreader, entry.ContentType);

        await StreamToResponse(entry.Spreader);
        return new EmptyResult();
    }

    /// <summary>
    ///     Returns a proper 206 response from completed cached data when a browser seeks. Callers must check
    ///     <see cref="StreamSpreader.Closed" /> first: a range served off a still-growing buffer would report a
    ///     total length that is already wrong by the time the client reads it.
    /// </summary>
    private async Task<IActionResult> BufferedRangeResponse(StreamSpreader streamSpreader, string contentType)
    {
        try
        {
            var bytes = await streamSpreader.GetBufferedBytesAsync(HttpContext.RequestAborted);
            return File(bytes, contentType, true);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return new EmptyResult();
        }
    }

    /// <summary>Pumps a stream spreader into the response body until the source closes or the client leaves.</summary>
    private async Task StreamToResponse(StreamSpreader streamSpreader)
    {
        // Captured once: these callbacks outlive the request, and touching HttpContext after it is
        // disposed throws inside the spreader instead of just unsubscribing us.
        var cancellationToken = HttpContext.RequestAborted;
        var body = Response.Body;

        var buffered = new ConcurrentQueue<(byte[], int, int)>();
        var finished = new SemaphoreSlim(0, 1);
        var syncSemaphore = new SemaphoreSlim(1, 1);

        var streamSubscriber = new StreamSubscriber
        {
            WriteCall = (bytes, offset, length) =>
            {
                buffered.Enqueue((bytes, offset, length));
                return Task.FromResult(cancellationToken.IsCancellationRequested
                    ? StreamStatus.Closed
                    : StreamStatus.Open);
            },
            SyncCall = SyncCall,
            CloseCall = async () =>
            {
                await SyncCall();
                finished.Release();
            }
        };

        await streamSpreader.SubscribeAsync(streamSubscriber);

        try
        {
            await finished.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await body.FlushAsync(cancellationToken);
        return;

        async Task SyncCall()
        {
            if (cancellationToken.IsCancellationRequested) return;
            await syncSemaphore.WaitAsync(CancellationToken.None);

            try
            {
                while (buffered.TryDequeue(out var entry))
                {
                    var (bytes, offset, length) = entry;
                    await body.WriteAsync(bytes.AsMemory(offset, length), cancellationToken);
                }
            }
            finally
            {
                syncSemaphore.Release();
            }
        }
    }
}