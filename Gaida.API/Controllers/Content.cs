using System.Collections.Concurrent;
using System.Diagnostics;
using Gaida.API.Contracts;
using Gaida.Core;
using Gaida.Core.FFmpeg;
using Gaida.Core.Platforms;
using Gaida.Core.Streams;
using Gaida.Platforms.MusicDatabase;
using Gaida.Platforms.YouTube;
using Microsoft.AspNetCore.Mvc;

namespace Gaida.API.Controllers;

[ApiController]
[Route("[controller]")]
public class Content(ILogger<Content> logger, IConfiguration configuration, IHostEnvironment environment) : ControllerBase
{
    /// <summary>Default share of a random request served from YouTube; the rest comes from the local library.</summary>
    private const double YouTubeShare = 0.4;

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
        [FromServices] ManagerService managerService, int count = 10, double youTubeShare = YouTubeShare)
    {
        if (count is < 1 or > 200)
            return BadRequest(new ApiErrorBody(new ApiError("invalid_count", "count must be between 1 and 200.")));

        if (youTubeShare is < 0 or > 1 or double.NaN)
            return BadRequest(new ApiErrorBody(new ApiError("invalid_share", "youTubeShare must be between 0 and 1.")));

        logger.LogInformation("Returning {Count} random results with a {Share} YouTube share", count, youTubeShare);
        var manager = managerService.Manager;
        var results = new List<SearchResultDto>();

        // Randomized rounding preserves the requested share over time while allowing either source
        // to be selected for small requests (for example, count=1 chooses YouTube 40% of the time).
        var exactYouTubeCount = count * youTubeShare;
        var youTubeCount = (int)Math.Floor(exactYouTubeCount);
        if (Random.Shared.NextDouble() < exactYouTubeCount - youTubeCount)
            youTubeCount++;

        await AddMappedResults(results, manager.GetPlatform<YouTube>()
            .GetRandomResults(youTubeCount, HttpContext.RequestAborted));

        // Local backfills whatever the YouTube cache was short of, so the caller always gets `count` results.
        await AddMappedResults(results, manager.GetPlatform<MusicDatabase>()
            .GetRandomResults(count - results.Count, HttpContext.RequestAborted));

        var shuffled = results.ToArray();
        Random.Shared.Shuffle(shuffled);
        return Ok(shuffled);
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

        var (contentType, ffmpegCodec, ffmpegOutputFormat) = Encoding(codec);

        var key = ManagerService.EncoderKey(codec, bitrate, id);
        if (!managerService.TryGetEncoder(key, out var encoderTask))
        {
            var result = await managerService.Manager.SearchID(id, HttpContext.RequestAborted);
            if (result is null) return NotFound("Search resulted in error");

            encoderTask = StartEncode(managerService, key, result, bitrate, ffmpegCodec, ffmpegOutputFormat, out _);
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

    /// <summary>
    ///     Runs the same encode <see cref="Download" /> would, without a body: the next Download for the same
    ///     codec/bitrate/id then finds it already running and streams what has been produced so far.
    /// </summary>
    [HttpGet]
    [Route("/Audio/Preload/{codec:required}/{bitrate:int:required}")]
    public async Task<IActionResult> Preload(string codec, int bitrate, string id,
        [FromServices] ManagerService managerService)
    {
        if (bitrate < 8) return BadRequest("Bitrate must be greater than 8");
        if (string.IsNullOrWhiteSpace(id)) return NotFound("No ID provided");

        // 200 for every caller after the first: the encode is already running or finished, and the lookup
        // has just pushed its expiry back, which is the whole of what a repeat preload can usefully do.
        var key = ManagerService.EncoderKey(codec, bitrate, id);
        if (managerService.TryGetEncoder(key, out _)) return Ok();

        logger.LogInformation("Preloading '{Id}' {Codec} {Bitrate}", id, codec, bitrate);
        var result = await managerService.Manager.SearchID(id, HttpContext.RequestAborted);
        if (result is null) return NotFound("Search resulted in error");

        var (_, ffmpegCodec, ffmpegOutputFormat) = Encoding(codec);
        // Awaited, not fired and forgotten: the start only runs as far as spawning ffmpeg and subscribing it to
        // the source, so this returns long before any audio is encoded, and a failure is still a 500 here
        // rather than an unobserved task exception. Cancelling the preload never cancels the encode.
        var encoder = await StartEncode(managerService, key, result, bitrate, ffmpegCodec, ffmpegOutputFormat,
            out var started);
        if (encoder is null) return StatusCode(500);

        // Two callers can reach this together and both find TryGetEncoder empty; only one of them added the
        // entry, and only that one gets the 202.
        return started ? Accepted() : Ok();
    }

    /// <summary>The ffmpeg arguments and response content type for a codec name, defaulting to Opus in Matroska.</summary>
    private static (string ContentType, string FfmpegCodec, string OutputFormat) Encoding(string codec)
    {
        return codec switch
        {
            "Opus" => ("audio/ogg", "-c:a libopus", "-f ogg"),
            "Vorbis" => ("audio/ogg", "-c:a libvorbis", "-f ogg"),
            "AAC" => ("audio/aac", "-c:a aac", "-f adts"),
            "FLAC" => ("audio/flac", "-c:a flac", "-f flac"),
            "MP3" => ("audio/mpeg", "-c:a libmp3lame", "-f mp3"),
            _ => ("audio/mka", "-c:a libopus", "-f mka")
        };
    }

    /// <summary>Starts the shared encode for a key, or hands back the one a racing request already started.</summary>
    private static Task<FFmpegEncoder?> StartEncode(ManagerService managerService, string key, PlatformResult result,
        int bitrate, string ffmpegCodec, string ffmpegOutputFormat, out bool started)
    {
        return managerService.GetOrStartEncoderAsync(key, async encoder =>
        {
            var sourceStreamSpreader = await managerService.Manager.GetContentDataAsync(result, CancellationToken.None);
            if (sourceStreamSpreader is null) return false;

            var sourceStreamSubscriber = encoder.Convert(bitrate, ffmpegCodec, ffmpegOutputFormat);
            if (sourceStreamSubscriber is null) return false;

            await sourceStreamSpreader.SubscribeAsync(sourceStreamSubscriber);
            return true;
        }, out started);
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
