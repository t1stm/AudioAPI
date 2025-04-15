using System.Collections.Concurrent;
using System.Net;
using Audio;
using Audio.FFmpeg;
using AudioManager.Platforms;
using AudioManager.Platforms.MusicDatabase;
using AudioManager.Streams;
using Microsoft.AspNetCore.Mvc;
using Result.Objects;
using AudioAPI;

namespace AudioAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class Content(ILogger<Content> logger) : ControllerBase
{
    [HttpGet]
    [Route("/Audio/Search")]
    public async Task<IActionResult> Search(string query, [FromServices] ManagerService manager_service)
    {
        if (string.IsNullOrWhiteSpace(query)) return NotFound();
        logger.LogInformation("Searching for {Query}", query);

        var query_type = manager_service.AudioManager.FindQueryType(query);

        switch (query_type)
        {
            case QueryType.ID:
                {
                    var split_query = query.Split("://");
                    var pure_id = split_query.Length > 1 ?
                        string.Join("://", split_query[1..]) : split_query[0];

                    var found = await manager_service.AudioManager.SearchID(pure_id);
                    if (found == Status.Error) return NotFound();
                    return Content(found.GetOK().SerializeSelf(), "application/json");
                }

            case QueryType.Playlist:
                {
                    var search = await manager_service.AudioManager.SearchPlaylist(query);
                    if (search == Status.Error) return NotFound();

                    var found = search.GetOK();
                    return Content(found.ToJSON(), "application/json");
                }

            case QueryType.Keywords:
                {
                    var search = await manager_service.AudioManager.SearchKeywords(query);
                    if (search == Status.Error) return NotFound();

                    var found = search.GetOK();
                    return Content(found.ToJSON(), "application/json");
                }

            default:
                return new StatusCodeResult(403);
        }
    }

    [HttpGet]
    [Route("/Audio/RandomResults")]
    public async Task<IActionResult> Search([FromServices] ManagerService manager_service, int count = 10)
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

        var split_query = id.Split("://");
        var pure_id = split_query.Length > 1 ?
            string.Join("://", split_query[1..]) : split_query[0];

        var stream_spreader = content_downloader_request.GetOK();
        var cache = new ConcurrentQueue<(byte[], int, int)>();

        var file_id = WebUtility.UrlEncode(pure_id);
        Response.Headers.Append("Content-Disposition", $"attachment; filename={file_id}");
        Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
        Response.Headers.ETag = $"raw-{file_id}";

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
    public async Task<IActionResult> Download(string codec, int bitrate, string id, [FromServices] ManagerService manager_service)
    {
        if (string.IsNullOrWhiteSpace(id)) return NotFound();
        logger.LogInformation("Downloading \'{Id}\' {Codec} {Bitrate}", codec, bitrate, id);

        var type = codec switch
        {
            "Opus" or "Vorbis" => "audio/ogg",
            "FLAC" => "audio/flac",
            "AAC" => "audio/aac",
            _ => "audio/mp3"
        };
        Response.ContentType = type;

        var ffmpeg_codec = codec switch
        {
            "Vorbis" => "-c:a libvorbis",
            "AAC" => "-c:a aac",
            "FLAC" => "-c:a flac",
            "MP3" => "-c:a libmp3lame",
            _ => "-c:a libopus"
        };

        var ffmpeg_output_format = codec switch
        {
            "Opus" or "Vorbis" => "-f ogg",
            "AAC" => "-f adts",
            "MP3" => "-f mp3",
            "FLAC" => "-f flac",
            _ => "-f mka"
        };

        var extension = ffmpeg_output_format[3..];

        await manager_service.CacheSemaphore.WaitAsync();
        if (!manager_service.CachedEncoders.TryGetValue((codec, bitrate, id), out var encoder))
        {
            encoder = new FFmpegEncoder();

            var search = await manager_service.AudioManager.SearchID(id);
            if (search == Status.Error) return NotFound();

            var result = search.GetOK();

            var content_downloader_request =
                await manager_service.AudioManager.TryGetContentData(result);

            if (content_downloader_request == Status.Error)
                return StatusCode(500);

            manager_service.CachedEncoders.Add((codec, bitrate, id), encoder);
            manager_service.CacheSemaphore.Release();

            var source_stream_spreader = content_downloader_request.GetOK();
            var stream_subscriber_result = encoder.Convert(bitrate, ffmpeg_codec, ffmpeg_output_format);

            if (stream_subscriber_result == Status.Error) return StatusCode(500);

            var source_stream_subscriber = stream_subscriber_result.GetOK();
            await source_stream_spreader.SubscribeAsync(source_stream_subscriber);
        }
        else manager_service.CacheSemaphore.Release();

        var cache = new ConcurrentQueue<(byte[], int, int)>();
        var waiting_semaphore = new SemaphoreSlim(0);
        var sync_semaphore = new SemaphoreSlim(1);
        var encoder_stream_spreader = encoder.GetStreamSpreader();

        var split_query = id.Split("://");
        var pure_id = split_query.Length > 1 ?
            string.Join("://", split_query[1..]) : split_query[0];

        var file_id = WebUtility.UrlEncode(pure_id);
        Response.Headers.Append("Content-Disposition", $"attachment; filename={file_id}.{extension}");
        Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
        Response.Headers.ETag = $"{type}-{bitrate}-{file_id}";

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
            manager_service.ExpireTimes.TryAdd((codec, bitrate, id), DateTime.Now.Add(TimeSpan.FromMinutes(45)));
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