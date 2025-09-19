using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using Audio;
using AudioManager.Platforms;
using AudioManager.Platforms.MusicDatabase;
using AudioManager.Streams;
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
    public async IAsyncEnumerable<PlatformResult> Search(string query, [FromServices] ManagerService manager_service)
    {
        if (string.IsNullOrWhiteSpace(query)) yield break;
        logger.LogInformation("Searching for {Query}", query);

        var query_type = manager_service.AudioManager.FindQueryType(query);
        
        switch (query_type)
        {
            case QueryType.ID:
                {
                    var idSpan = query.AsSpan();
                    Span<Range> ranges = stackalloc Range[2];
        
                    var count = idSpan.Split(ranges, "://");
                    var pureId = count > 1 ? idSpan[ranges[1]]: idSpan;

                    var found = await manager_service.AudioManager.SearchID(pureId.ToString()); // TODO: search methods should use ReadOnlySpan<char> wherever possible
                    if (found == Status.Error)
                        yield break;
                    
                    yield return found.GetOK();
                    break;
                }

            case QueryType.Playlist:
                {
                    await foreach (var result in manager_service.AudioManager.SearchPlaylist(query))
                    {
                        yield return result;
                    }
                    break;
                }

            case QueryType.Keywords:
                {
                    await foreach (var result in manager_service.AudioManager.SearchKeywords(query))
                    {
                        yield return result;
                    }
                    break;
                }
        }
    }

    [HttpGet]
    [Route("/Audio/RandomResults")]
    public async Task<IActionResult> RandomResults([FromServices] ManagerService manager_service, int count = 10)
    {
        var platform = manager_service.AudioManager.GetPlatform<MusicDatabase>();
        logger.LogInformation("Returning {Count} random results", count);
        var results = await platform.GetRandomResults(count);
        if (results == Status.Error) return NotFound();

        var ok = results.GetOK();
        return Content(ok.ToJSON(), "application/json");
    }

    [HttpGet]
    [Route("/Audio/DownloadRaw")]
    [Produces("audio/ogg", "audio/mp3", "audio/aac", "audio/flac", "audio/mka", "audio/webm", "text/plain")]
    public async Task<IActionResult> DownloadRaw(string id, [FromServices] ManagerService manager_service)
    {
        if (string.IsNullOrWhiteSpace(id)) return NotFound();
        logger.LogInformation("Downloading Raw \'{Id}\'", id);

        var start = DateTime.Now;
        var search = await manager_service.AudioManager.SearchID(id);
        if (search == Status.Error) return NotFound();

        var result = search.GetOK();

        var found_result = DateTime.Now;
        logger.LogInformation("Searching \'{Id}\' took \'{Duration}\'", id, found_result - start);

        var content_downloader_request =
            await manager_service.AudioManager.TryGetContentData(result);
        if (content_downloader_request == Status.Error)
            return StatusCode(500);

        var stream_spreader = content_downloader_request.GetOK();
        var cache = new ConcurrentQueue<(byte[], int, int)>();

        var idSpan = id.AsSpan();
        Span<Range> ranges = stackalloc Range[2];
        
        var count = idSpan.Split(ranges, "://");
        var pureId = count > 1 ? idSpan[ranges[1]]: idSpan;

        var rentArray = ArrayPool<char>.Shared.Rent(pureId.Length);
        var rentBuffer = rentArray.AsSpan();
        var urlEncoder = UrlEncoder.Default;

        urlEncoder.Encode(pureId, rentBuffer, out _, out var written);
        ReadOnlySpan<char> fileId = rentBuffer[..written];
        
        Response.Headers.Append("Content-Disposition", (string)$"attachment; filename={fileId}");
        Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
        Response.Headers.ETag = (string)$"raw-{fileId}";

        var waiting_semaphore = new SemaphoreSlim(0, 1);
        var sync_semaphore = new SemaphoreSlim(1, 1);

        var stream_subscriber = new StreamSubscriber
        {
            WriteCall = (bytes, offset, length) =>
            {
                cache.Enqueue((bytes, offset, length));
                return Task.FromResult(HttpContext.RequestAborted.IsCancellationRequested ?
                    StreamStatus.Closed : StreamStatus.Open);
            },
            SyncCall = SyncCall,
            CloseCall = () =>
            {
                waiting_semaphore.Release();
                return Task.CompletedTask;
            }
        };

        var subscribed = DateTime.Now;
        await stream_spreader.SubscribeAsync(stream_subscriber);

        await waiting_semaphore.WaitAsync();
        await Response.Body.FlushAsync();

        var finish = DateTime.Now;
        logger.LogInformation(
            "Finishing \'{Id}\' took: \'{Duration}\', with the time while subscribed being \'{Time}\'",
            id, finish - start, finish - subscribed);
        return new EmptyResult();

        async Task SyncCall()
        {
            if (HttpContext.RequestAborted.IsCancellationRequested) return;
            await sync_semaphore.WaitAsync();

            while (cache.TryDequeue(out var entry))
            {
                var (bytes, offset, length) = entry;
                await Response.Body.WriteAsync(bytes.AsMemory(offset, length));
            }

            sync_semaphore.Release();
        }
    }

    [HttpGet]
    [Route("/Audio/Download/{codec:required}/{bitrate:int:required}")]
    [Produces("audio/ogg", "audio/mp3", "audio/aac", "audio/flac", "audio/mka", "audio/webm", "text/plain")]
    public async Task<IActionResult> Download(string codec, int bitrate, string id, [FromServices] ManagerService manager_service)
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
        
        if (!manager_service.TryGetEncoder(codec, bitrate, id, out var encoder))
        {
            var search = await manager_service.AudioManager.SearchID(id);
            if (search == Status.Error) return NotFound("Search resulted in error");

            var result = search.GetOK();

            var content_downloader_request =
                await manager_service.AudioManager.TryGetContentData(result);

            if (content_downloader_request == Status.Error)
                return StatusCode(500);

            (_, encoder) = manager_service.CreateNewEncoder(codec, bitrate, id);

            var source_stream_spreader = content_downloader_request.GetOK();
            var stream_subscriber_result = encoder.Convert(bitrate, ffmpegCodec, ffmpegOutputFormat);

            if (stream_subscriber_result == Status.Error) return StatusCode(500);

            var source_stream_subscriber = stream_subscriber_result.GetOK();
            await source_stream_spreader.SubscribeAsync(source_stream_subscriber);
        }

        var cache = new ConcurrentQueue<(byte[], int, int)>();
        var waiting_semaphore = new SemaphoreSlim(0);
        var sync_semaphore = new SemaphoreSlim(1);
        var encoder_stream_spreader = encoder.GetStreamSpreader();
        
        var idSpan = id.AsSpan();
        Span<Range> ranges = stackalloc Range[2];
        
        var count = idSpan.Split(ranges, "://");
        var pureId = count > 1 ? idSpan[ranges[1]]: idSpan;

        var rentArray = ArrayPool<char>.Shared.Rent(pureId.Length);
        var rentBuffer = rentArray.AsSpan();
        var urlEncoder = UrlEncoder.Default;

        urlEncoder.Encode(pureId, rentBuffer, out _, out var written);
        ReadOnlySpan<char> fileId = rentBuffer[..written];
        
        Response.Headers.Append("Content-Disposition", (string)$"attachment; filename={fileId}.{extension}");
        Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
        Response.Headers.ETag = (string)$"{type}-{bitrate}-{fileId}";
        
        ArrayPool<char>.Shared.Return(rentArray);
        var stream_subscriber = new StreamSubscriber
        {
            WriteCall = (bytes, offset, length) =>
            {
                cache.Enqueue((bytes, offset, length));
                return Task.FromResult(HttpContext.RequestAborted.IsCancellationRequested ?
                    StreamStatus.Closed : StreamStatus.Open);
            },
            SyncCall = SyncCall,
            CloseCall = CloseCall
        };
        await encoder_stream_spreader.SubscribeAsync(stream_subscriber);
        await waiting_semaphore.WaitAsync();

        await Response.Body.FlushAsync();
        return new EmptyResult();

        async Task CloseCall()
        {
            await sync_semaphore.WaitAsync();
            sync_semaphore.Release();

            await SyncCall();
            waiting_semaphore.Release();
            manager_service.AddNewExpireSession(encoder, DateTime.Now.Add(TimeSpan.FromMinutes(45)));
        }

        async Task SyncCall()
        {
            if (HttpContext.RequestAborted.IsCancellationRequested) return;
            if (sync_semaphore.CurrentCount == 0) return;

            await sync_semaphore.WaitAsync();

            while (cache.TryDequeue(out var entry))
            {
                var (bytes, offset, length) = entry;
                await Response.Body.WriteAsync(bytes.AsMemory(offset, length));
            }

            sync_semaphore.Release();
        }
    }
}