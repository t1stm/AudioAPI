using System.Collections.Concurrent;
using System.Diagnostics;
using Gaida.API.Contracts;
using Gaida.Core;
using Gaida.Core.Platforms;
using Gaida.Core.Streams;
using Gaida.Platforms.MusicDatabase;
using Microsoft.AspNetCore.Mvc;

namespace Gaida.API.Controllers;

[ApiController]
[Route("[controller]")]
public class Content(ILogger<Content> logger, IConfiguration configuration, IHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    [Route("/Audio/Search")]
    [Produces("application/json")]
    public async Task<ActionResult<IReadOnlyList<SearchResultDto>>> Search(string? query,
        [FromServices] ManagerService managerService)
    {
        if (string.IsNullOrWhiteSpace(query)) return Ok(Array.Empty<SearchResultDto>());
        logger.LogInformation("Searching for {Query}", query);

        var manager = managerService.Manager;
        var cancellationToken = HttpContext.RequestAborted;
        var results = new List<SearchResultDto>();

        try
        {
            switch (manager.FindQueryType(query))
            {
                case QueryType.ID:
                {
                    var found = await manager.SearchID(query, cancellationToken);
                    AddMappedResult(results, found);
                    break;
                }

                case QueryType.Playlist:
                    await AddMappedResults(results, manager.SearchPlaylist(query, cancellationToken));
                    break;

                case QueryType.Keywords:
                default:
                    await AddMappedResults(results, manager.SearchKeywords(query, cancellationToken));
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Discovery remains a valid JSON response even when an upstream provider is unavailable.
            logger.LogError(exception, "Search failed for {Query}", query);
        }

        return Ok(results);
    }

    [HttpGet]
    [Route("/Audio/RandomResults")]
    [Produces("application/json")]
    public async Task<ActionResult<IReadOnlyList<SearchResultDto>>> RandomResults(
        [FromServices] ManagerService managerService, int count = 10)
    {
        if (count is < 1 or > 200)
            return BadRequest(new ApiErrorBody(new ApiError("invalid_count", "count must be between 1 and 200.")));

        logger.LogInformation("Returning {Count} random results", count);
        var results = new List<SearchResultDto>();
        await AddMappedResults(results, managerService.Manager.GetPlatform<MusicDatabase>()
            .GetRandomResults(count, HttpContext.RequestAborted));
        return Ok(results);
    }

    [HttpGet]
    [Route("/Audio/DownloadRaw")]
    [Produces("audio/ogg", "audio/mpeg", "audio/aac", "audio/flac", "audio/mka", "audio/webm", "text/plain")]
    public async Task<IActionResult> DownloadRaw(string id, [FromServices] ManagerService managerService)
    {
        if (string.IsNullOrWhiteSpace(id)) return NotFound();
        logger.LogInformation("Downloading Raw '{Id}'", id);

        var start = Stopwatch.GetTimestamp();
        var result = await managerService.Manager.SearchID(id, HttpContext.RequestAborted);
        if (result is null) return NotFound();

        var streamSpreader = await managerService.Manager.GetContentDataAsync(result, HttpContext.RequestAborted);
        if (streamSpreader is null) return StatusCode(500);

        var fileId = FileId(id);
        var extension = result is MusicResult localResult ? Path.GetExtension(localResult.Path) : ".audio";
        SetDownloadHeaders(fileId + extension, $"raw-{fileId}", streamSpreader.Closed);

        if (streamSpreader.Closed && Request.Headers.Range.Count > 0)
            return await BufferedRangeResponse(streamSpreader, "application/octet-stream");

        await StreamToResponse(streamSpreader);

        logger.LogInformation("Finishing '{Id}' took '{Duration}'", id, Stopwatch.GetElapsedTime(start));
        return new EmptyResult();
    }

    [HttpGet]
    [Route("/Audio/Download/{codec:required}/{bitrate:int:required}")]
    [Produces("audio/ogg", "audio/mp3", "audio/aac", "audio/flac", "audio/mka", "audio/webm", "text/plain")]
    public async Task<IActionResult> Download(string codec, int bitrate, string id,
        [FromServices] ManagerService managerService)
    {
        if (bitrate < 8) return BadRequest("Bitrate must be greater than 8");
        if (string.IsNullOrWhiteSpace(id)) return NotFound("No ID provided");
        logger.LogInformation("Downloading '{Id}' {Codec} {Bitrate}", id, codec, bitrate);

        var (contentType, ffmpegCodec, ffmpegOutputFormat) = codec switch
        {
            "Opus" => ("audio/ogg", "-c:a libopus", "-f ogg"),
            "Vorbis" => ("audio/ogg", "-c:a libvorbis", "-f ogg"),
            "AAC" => ("audio/aac", "-c:a aac", "-f adts"),
            "FLAC" => ("audio/flac", "-c:a flac", "-f flac"),
            "MP3" => ("audio/mpeg", "-c:a libmp3lame", "-f mp3"),
            _ => ("audio/mka", "-c:a libopus", "-f mka")
        };

        var key = ManagerService.EncoderKey(codec, bitrate, id);
        if (!managerService.TryGetEncoder(key, out var encoderTask))
        {
            var result = await managerService.Manager.SearchID(id, HttpContext.RequestAborted);
            if (result is null) return NotFound("Search resulted in error");

            encoderTask = managerService.GetOrStartEncoderAsync(key, async encoder =>
            {
                // Deliberately not RequestAborted: this encode is shared by every request for the same key,
                // so the client that happened to start it must not cancel it for the others on the way out.
                var sourceStreamSpreader =
                    await managerService.Manager.GetContentDataAsync(result, CancellationToken.None);
                if (sourceStreamSpreader is null) return false;

                var sourceStreamSubscriber = encoder.Convert(bitrate, ffmpegCodec, ffmpegOutputFormat);
                if (sourceStreamSubscriber is null) return false;

                await sourceStreamSpreader.SubscribeAsync(sourceStreamSubscriber);
                return true;
            });
        }

        var startedEncoder = await encoderTask;
        if (startedEncoder is null) return StatusCode(500);

        var encodedStream = startedEncoder.GetStreamSpreader();
        var fileId = FileId(id);
        var outputFileName = $"{fileId}.{ffmpegOutputFormat[3..]}";
        SetStreamHeaders(contentType, outputFileName, $"{contentType}-{bitrate}-{fileId}", encodedStream.Closed);

        if (encodedStream.Closed && Request.Headers.Range.Count > 0)
            return await BufferedRangeResponse(encodedStream, contentType);

        await StreamToResponse(encodedStream);
        return new EmptyResult();
    }

    /// <summary>The ID without its platform protocol, safe to put in a header.</summary>
    private static string FileId(string id)
    {
        var separator = id.IndexOf("://", StringComparison.Ordinal);
        var value = separator >= 0 ? id[(separator + 3)..] : id;
        return Uri.EscapeDataString(value);
    }

    private void SetDownloadHeaders(string fileName, string etag, bool seekable)
    {
        Response.Headers.Append("Content-Disposition", $"attachment; filename={fileName}");
        SetRangeSupport(seekable);
        SetCacheHeaders(etag);
    }

    private void SetStreamHeaders(string contentType, string fileName, string etag, bool seekable)
    {
        Response.ContentType = contentType;
        Response.Headers.Append("Content-Disposition", $"inline; filename={fileName}");
        SetRangeSupport(seekable);
        SetCacheHeaders(etag);
    }

    /// <summary>Only claim range support for a finished body: promising it mid-encode makes players seek into a 200.</summary>
    private void SetRangeSupport(bool seekable)
    {
        Response.Headers.AcceptRanges = seekable ? "bytes" : "none";
    }

    private void SetCacheHeaders(string etag)
    {
        Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
        Response.Headers.ETag = $"\"{etag}\"";
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
            return File(bytes, contentType, enableRangeProcessing: true);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return new EmptyResult();
        }
    }

    private void AddMappedResult(ICollection<SearchResultDto> destination, PlatformResult? result)
    {
        if (result is null) return;
        var mapped = DiscoveryResultMapper.Map(result, Request, configuration, environment);
        if (mapped is not null) destination.Add(mapped);
    }

    private async Task AddMappedResults(ICollection<SearchResultDto> destination,
        IAsyncEnumerable<PlatformResult> source)
    {
        await foreach (var result in source.WithCancellation(HttpContext.RequestAborted))
            AddMappedResult(destination, result);
    }

    /// <summary>Pumps a stream spreader into the response body until the source closes or the client leaves.</summary>
    private async Task StreamToResponse(StreamSpreader streamSpreader)
    {
        // Captured once: these callbacks outlive the request, and touching HttpContext after it is
        // disposed throws inside the spreader instead of just unsubscribing us.
        var cancellationToken = HttpContext.RequestAborted;
        var body = Response.Body;

        var cache = new ConcurrentQueue<(byte[], int, int)>();
        var finished = new SemaphoreSlim(0, 1);
        var syncSemaphore = new SemaphoreSlim(1, 1);

        var streamSubscriber = new StreamSubscriber
        {
            WriteCall = (bytes, offset, length) =>
            {
                cache.Enqueue((bytes, offset, length));
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
                while (cache.TryDequeue(out var entry))
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
