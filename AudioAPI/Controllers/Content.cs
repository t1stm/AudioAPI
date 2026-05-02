using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using AudioManagement;
using AudioManagement.Platforms;
using AudioManagement.Platforms.MusicDatabase;
using AudioManagement.Streams;
using Microsoft.AspNetCore.Mvc;
using Result.Objects;

namespace AudioAPI.Controllers;

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

        var queryType = managerService.Manager.FindQueryType(query);

        switch (queryType)
        {
            case QueryType.ID:
            {
                var idSpan = query.AsSpan();
                Span<Range> ranges = stackalloc Range[2];

                var count = idSpan.Split(ranges, "://");
                var pureId = count > 1 ? idSpan[ranges[1]] : idSpan;

                var found = await managerService.Manager
                    .SearchID(pureId
                        .ToString()); // TODO: search methods should use ReadOnlySpan<char> wherever possible
                if (found == Status.Error)
                    yield break;

                yield return found.GetOk();
                break;
            }

            case QueryType.Playlist:
            {
                await foreach (var result in managerService.Manager.SearchPlaylist(query)) yield return result;
                break;
            }

            case QueryType.Keywords:
            default:
            {
                await foreach (var result in managerService.Manager.SearchKeywords(query)) yield return result;
                break;
            }
        }
    }

    [HttpGet]
    [Route("/Audio/RandomResults")]
    public async Task<IActionResult> RandomResults([FromServices] ManagerService managerService, int count = 10)
    {
        var platform = managerService.Manager.GetPlatform<MusicDatabase>();
        logger.LogInformation("Returning {Count} random results", count);
        var results = await platform.GetRandomResults(count);
        if (results == Status.Error) return NotFound();

        var ok = results.GetOk();
        return Content(ok.ToJson(), "application/json");
    }

    [HttpGet]
    [Route("/Audio/DownloadRaw")]
    [Produces("audio/ogg", "audio/mp3", "audio/aac", "audio/flac", "audio/mka", "audio/webm", "text/plain")]
    public async Task<IActionResult> DownloadRaw(string id, [FromServices] ManagerService managerService)
    {
        if (string.IsNullOrWhiteSpace(id)) return NotFound();
        logger.LogInformation("Downloading Raw \'{Id}\'", id);

        var start = DateTime.Now;
        var search = await managerService.Manager.SearchID(id);
        if (search == Status.Error) return NotFound();

        var result = search.GetOk();

        var foundResult = DateTime.Now;
        logger.LogInformation("Searching \'{Id}\' took \'{Duration}\'", id, foundResult - start);

        var contentDownloaderRequest =
            await managerService.Manager.TryGetContentData(result);
        if (contentDownloaderRequest == Status.Error)
            return StatusCode(500);

        var streamSpreader = contentDownloaderRequest.GetOk();
        var cache = new ConcurrentQueue<(byte[], int, int)>();

        var idSpan = id.AsSpan();
        Span<Range> ranges = stackalloc Range[2];

        var count = idSpan.Split(ranges, "://");
        var pureId = count > 1 ? idSpan[ranges[1]] : idSpan;

        var rentArray = ArrayPool<char>.Shared.Rent(pureId.Length);
        var rentBuffer = rentArray.AsSpan();
        var urlEncoder = UrlEncoder.Default;

        urlEncoder.Encode(pureId, rentBuffer, out _, out var written);
        ReadOnlySpan<char> fileId = rentBuffer[..written];

        Response.Headers.Append("Content-Disposition", (string)$"attachment; filename={fileId}");
        Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
        Response.Headers.ETag = (string)$"raw-{fileId}";

        var waitingSemaphore = new SemaphoreSlim(0, 1);
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
            CloseCall = () =>
            {
                waitingSemaphore.Release();
                return Task.CompletedTask;
            }
        };

        var subscribed = DateTime.Now;
        await streamSpreader.SubscribeAsync(streamSubscriber);

        await waitingSemaphore.WaitAsync();
        await Response.Body.FlushAsync();

        var finish = DateTime.Now;
        logger.LogInformation(
            "Finishing \'{Id}\' took: \'{Duration}\', with the time while subscribed being \'{Time}\'",
            id, finish - start, finish - subscribed);
        return new EmptyResult();

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

    [HttpGet]
    [Route("/Audio/Download/{codec:required}/{bitrate:int:required}")]
    [Produces("audio/ogg", "audio/mp3", "audio/aac", "audio/flac", "audio/mka", "audio/webm", "text/plain")]
    public async Task<IActionResult> Download(string codec, int bitrate, string id,
        [FromServices] ManagerService managerService)
    {
        if (bitrate < 8) return BadRequest("Bitrate must be greater than 8");
        if (string.IsNullOrWhiteSpace(id)) return NotFound("No ID provided");
        logger.LogInformation("Downloading \'{Id}\' {Codec} {Bitrate}", id, codec, bitrate);

        var type = codec switch
        {
            "Opus" or "Vorbis" => "audio/ogg",
            "FLAC" => "audio/flac",
            "AAC" => "audio/aac",
            _ => "audio/mp3"
        };
        Response.ContentType = type;

        var ffmpegCodec = codec switch
        {
            "Vorbis" => "-c:a libvorbis",
            "AAC" => "-c:a aac",
            "FLAC" => "-c:a flac",
            "MP3" => "-c:a libmp3lame",
            _ => "-c:a libopus"
        };

        var ffmpegOutputFormat = codec switch
        {
            "Opus" or "Vorbis" => "-f ogg",
            "AAC" => "-f adts",
            "MP3" => "-f mp3",
            "FLAC" => "-f flac",
            _ => "-f mka"
        };

        var extension = ffmpegOutputFormat[3..];

        if (!managerService.TryGetEncoder(codec, bitrate, id, out var encoder))
        {
            var search = await managerService.Manager.SearchID(id);
            if (search == Status.Error) return NotFound("Search resulted in error");

            var result = search.GetOk();

            var contentDownloaderRequest =
                await managerService.Manager.TryGetContentData(result);

            if (contentDownloaderRequest == Status.Error)
                return StatusCode(500);

            (_, encoder) = managerService.CreateNewEncoder(codec, bitrate, id);

            var sourceStreamSpreader = contentDownloaderRequest.GetOk();
            var streamSubscriberResult = encoder.Convert(bitrate, ffmpegCodec, ffmpegOutputFormat);

            if (streamSubscriberResult == Status.Error) return StatusCode(500);

            var sourceStreamSubscriber = streamSubscriberResult.GetOk();
            await sourceStreamSpreader.SubscribeAsync(sourceStreamSubscriber);
        }

        var cache = new ConcurrentQueue<(byte[], int, int)>();
        var waitingSemaphore = new SemaphoreSlim(0);
        var syncSemaphore = new SemaphoreSlim(1);
        var encoderStreamSpreader = encoder.GetStreamSpreader();

        var idSpan = id.AsSpan();
        Span<Range> ranges = stackalloc Range[2];

        var count = idSpan.Split(ranges, "://");
        var pureId = count > 1 ? idSpan[ranges[1]] : idSpan;

        var rentArray = ArrayPool<char>.Shared.Rent(pureId.Length);
        var rentBuffer = rentArray.AsSpan();
        var urlEncoder = UrlEncoder.Default;

        urlEncoder.Encode(pureId, rentBuffer, out _, out var written);
        ReadOnlySpan<char> fileId = rentBuffer[..written];

        Response.Headers.Append("Content-Disposition", (string)$"attachment; filename={fileId}.{extension}");
        Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
        Response.Headers.ETag = (string)$"{type}-{bitrate}-{fileId}";

        ArrayPool<char>.Shared.Return(rentArray);
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
            CloseCall = CloseCall
        };
        await encoderStreamSpreader.SubscribeAsync(streamSubscriber);
        await waitingSemaphore.WaitAsync();

        await Response.Body.FlushAsync();
        return new EmptyResult();

        async Task CloseCall()
        {
            await syncSemaphore.WaitAsync();
            syncSemaphore.Release();

            await SyncCall();
            waitingSemaphore.Release();
            managerService.AddNewExpireSession(encoder, DateTime.Now.Add(TimeSpan.FromMinutes(45)));
        }

        async Task SyncCall()
        {
            if (HttpContext.RequestAborted.IsCancellationRequested) return;
            if (syncSemaphore.CurrentCount == 0) return;

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