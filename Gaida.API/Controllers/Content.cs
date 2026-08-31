using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Encodings.Web;
using Gaida.Core;
using Gaida.Core.Platforms;
using Gaida.Core.Streams;
using Gaida.Core.Utils;
using Gaida.Platforms.MusicDatabase;
using Microsoft.AspNetCore.Mvc;

namespace Gaida.API.Controllers;

[ApiController]
[Route("[controller]")]
public class Content(ILogger<Content> logger) : ControllerBase
{
    [HttpGet]
    [Route("/Audio/Search")]
    [Produces("application/json")]
    public async IAsyncEnumerable<PlatformResult> Search(string query, [FromServices] ManagerService managerService)
    {
        if (string.IsNullOrWhiteSpace(query)) yield break;
        logger.LogInformation("Searching for {Query}", query);

        var manager = managerService.Manager;
        var cancellationToken = HttpContext.RequestAborted;

        switch (manager.FindQueryType(query))
        {
            case QueryType.ID:
            {
                var found = await manager.SearchID(query, cancellationToken);
                if (found is not null) yield return found;
                break;
            }

            case QueryType.Playlist:
            {
                await foreach (var result in manager.SearchPlaylist(query, cancellationToken)) yield return result;
                break;
            }

            case QueryType.Keywords:
            default:
            {
                await foreach (var result in manager.SearchKeywords(query, cancellationToken)) yield return result;
                break;
            }
        }
    }

    [HttpGet]
    [Route("/Audio/RandomResults")]
    [Produces("application/json")]
    public IAsyncEnumerable<PlatformResult> RandomResults([FromServices] ManagerService managerService, int count = 10)
    {
        logger.LogInformation("Returning {Count} random results", count);
        return managerService.Manager.GetPlatform<MusicDatabase>()
            .GetRandomResults(count, HttpContext.RequestAborted);
    }

    [HttpGet]
    [Route("/Audio/DownloadRaw")]
    [Produces("audio/ogg", "audio/mp3", "audio/aac", "audio/flac", "audio/mka", "audio/webm", "text/plain")]
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
        SetDownloadHeaders(fileId, $"raw-{fileId}");

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
            "MP3" => ("audio/mp3", "-c:a libmp3lame", "-f mp3"),
            _ => ("audio/mka", "-c:a libopus", "-f mka")
        };

        Response.ContentType = contentType;

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
        SetDownloadHeaders($"{fileId}.{ffmpegOutputFormat[3..]}", $"{contentType}-{bitrate}-{fileId}");

        await StreamToResponse(encoder.GetStreamSpreader(),
            () => managerService.ExpireIn(key, TimeSpan.FromMinutes(45)));

        return new EmptyResult();
    }

    /// <summary>The ID without its platform protocol, safe to put in a header.</summary>
    private static string FileId(string id)
    {
        return UrlEncoder.Default.Encode(id.AsSpan().SliceAfter("://").ToString());
    }

    private void SetDownloadHeaders(string fileName, string etag)
    {
        Response.Headers.Append("Content-Disposition", $"attachment; filename={fileName}");
        Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
        Response.Headers.ETag = $"\"{etag}\"";
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
