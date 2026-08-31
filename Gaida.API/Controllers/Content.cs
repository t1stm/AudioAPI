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
        SetDownloadHeaders(fileId + extension, $"raw-{fileId}");

        if (Request.Headers.Range.Count > 0)
        {
            var rangeResponse = await BufferedRangeResponse(streamSpreader, "application/octet-stream");
            if (rangeResponse is not null) return rangeResponse;
        }

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
        if (!managerService.TryGetEncoder(key, out var encoder))
        {
            var result = await managerService.Manager.SearchID(id, HttpContext.RequestAborted);
            if (result is null) return NotFound("Search resulted in error");

            var sourceStreamSpreader =
                await managerService.Manager.GetContentDataAsync(result, HttpContext.RequestAborted);
            if (sourceStreamSpreader is null) return StatusCode(500);

            encoder = managerService.CreateEncoder(key);
            var sourceStreamSubscriber = encoder.Convert(bitrate, ffmpegCodec, ffmpegOutputFormat);
            if (sourceStreamSubscriber is null) return StatusCode(500);

            await sourceStreamSpreader.SubscribeAsync(sourceStreamSubscriber);
        }

        var fileId = FileId(id);
        var outputFileName = $"{fileId}.{ffmpegOutputFormat[3..]}";
        SetStreamHeaders(contentType, outputFileName, $"{contentType}-{bitrate}-{fileId}");

        var encodedStream = encoder.GetStreamSpreader();
        if (Request.Headers.Range.Count > 0)
        {
            var rangeResponse = await BufferedRangeResponse(encodedStream, contentType);
            if (rangeResponse is not null) return rangeResponse;
        }

        await StreamToResponse(encodedStream,
            () => managerService.ExpireIn(key, TimeSpan.FromMinutes(45)));

        return new EmptyResult();
    }

    /// <summary>The ID without its platform protocol, safe to put in a header.</summary>
    private static string FileId(string id)
    {
        var separator = id.IndexOf("://", StringComparison.Ordinal);
        var value = separator >= 0 ? id[(separator + 3)..] : id;
        return Uri.EscapeDataString(value);
    }

    private void SetDownloadHeaders(string fileName, string etag)
    {
        Response.Headers.Append("Content-Disposition", $"attachment; filename={fileName}");
        Response.Headers.AcceptRanges = "bytes";
        SetCacheHeaders(etag);
    }

    private void SetStreamHeaders(string contentType, string fileName, string etag)
    {
        Response.ContentType = contentType;
        Response.Headers.Append("Content-Disposition", $"inline; filename={fileName}");
        Response.Headers.AcceptRanges = "bytes";
        SetCacheHeaders(etag);
    }

    private void SetCacheHeaders(string etag)
    {
        Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
        Response.Headers.ETag = $"\"{etag}\"";
    }

    /// <summary>Returns a proper 206 response from completed cached data when a browser seeks.</summary>
    private async Task<IActionResult?> BufferedRangeResponse(StreamSpreader streamSpreader, string contentType)
    {
        try
        {
            await streamSpreader.WaitForCloseAsync(HttpContext.RequestAborted);
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
    private async Task StreamToResponse(StreamSpreader streamSpreader, Action? onFinished = null)
    {
        var cache = new ConcurrentQueue<(byte[], int, int)>();
        var finished = new SemaphoreSlim(0, 1);
        var syncSemaphore = new SemaphoreSlim(1, 1);

        var streamSubscriber = new StreamSubscriber
        {
            WriteCall = (bytes, offset, length) =>
            {
                cache.Enqueue((bytes, offset, length));
                return Task.FromResult(HttpContext.RequestAborted.IsCancellationRequested
                    ? StreamStatus.Closed
                    : StreamStatus.Open);
            },
            SyncCall = SyncCall,
            CloseCall = async () =>
            {
                await SyncCall();
                onFinished?.Invoke();
                finished.Release();
            }
        };

        await streamSpreader.SubscribeAsync(streamSubscriber);

        try
        {
            await finished.WaitAsync(HttpContext.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await Response.Body.FlushAsync();
        return;

        async Task SyncCall()
        {
            if (HttpContext.RequestAborted.IsCancellationRequested) return;
            await syncSemaphore.WaitAsync();

            while (cache.TryDequeue(out var entry))
            {
                var (bytes, offset, length) = entry;
                await Response.Body.WriteAsync(bytes.AsMemory(offset, length));
            }

            syncSemaphore.Release();
        }
    }
}
