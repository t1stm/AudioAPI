using System.Collections.Concurrent;
using Audio;
using Audio.FFmpeg;
using AudioManager.Streams;
using Microsoft.AspNetCore.Mvc;
using Result.Objects;

namespace AudioAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class Content : ControllerBase
{
    private readonly ILogger<Content> Logger;
    private readonly Audio.AudioManager AudioManager;
    private readonly ConcurrentDictionary<(string codec, int bitrate, string id), FFmpegEncoder> CachedEncoders = new();

    public Content(ILogger<Content> logger)
    {
        Logger = logger;
        AudioManager = new Audio.AudioManager();
        AudioManager.Initialize();
    }

    [HttpGet]
    [Route("/Audio/Search")]
    public async Task<IActionResult> Search(string query)
    {
        Logger.LogInformation("Searching for {Query}", query);
        
        var query_type = AudioManager.FindQueryType(query);
        switch (query_type)
        {
            case QueryType.ID:
            {
                var split_query = query.Split("://");
                var pure_id = split_query.Length > 1 ? 
                    string.Join("://", split_query[1..]) : split_query[0];
                
                var found = await AudioManager.SearchID(pure_id);
                if (found == Status.Error) return NotFound();
                return new JsonResult(found.GetOK());
            }

            case QueryType.Playlist:
            {
                var split_query = query.Split("://");
                var pure_id = split_query.Length > 1 ? 
                    string.Join("://", split_query[1..]) : split_query[0];
                
                // TODO
                return new JsonResult("TODO");
            }

            case QueryType.Keywords:
            {
                var search = await AudioManager.SearchKeywords(query);
                if (search == Status.Error) return NotFound();
                
                return new JsonResult(search.GetOK());
            }
            
            default: 
                return new EmptyResult();
        }
    }

    [HttpGet]
    [Route("/Audio/DownloadRaw")]
    public async Task<IActionResult> DownloadRaw(string id)
    {
        Logger.LogInformation("Downloading Raw \'{Id}\'", id);
        
        var search = await AudioManager.SearchID(id);
        if (search == Status.Error) return NotFound();
        
        var result = search.GetOK();
        
        var content_downloader_request = await AudioManager.GetContentDownloader(result);
        if (content_downloader_request == Status.Error) 
            return StatusCode(500);
        
        var split_query = id.Split("://");
        var pure_id = split_query.Length > 1 ? 
            string.Join("://", split_query[1..]) : split_query[0];
        
        var stream_spreader = content_downloader_request.GetOK();
        var cache = new ConcurrentQueue<(byte[], int, int)>();
        
        Response.Headers.Append("Content-Disposition", $"attachment; filename={pure_id}");
        Response.Headers.Append("Cache-Control", "no-cache; no-store; must-revalidate");
        
        var waiting_semaphore = new SemaphoreSlim(0, 1);
        var sync_semaphore = new SemaphoreSlim(1, 1);

        var stream_subscriber = new StreamSubscriber
        {
            WriteCall = (bytes, offset, length) =>
            {
                cache.Enqueue((bytes, offset, length));
                return HttpContext.RequestAborted.IsCancellationRequested ? 
                    StreamStatus.Closed : StreamStatus.Open;
            },
            SyncCall = SyncCall,
            CloseCall = () =>
            {
                waiting_semaphore.Release();
            }
        };
        stream_spreader.Subscribe(stream_subscriber);

        await waiting_semaphore.WaitAsync();
        await Response.Body.FlushAsync();
        return new EmptyResult();

        async void SyncCall()
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
    public async Task<IActionResult> Download(string codec, int bitrate, string id)
    {
        Logger.LogInformation("Downloading \'{Id}\' {Codec} {Bitrate}", codec, bitrate, id);
        
        var type = codec switch
        {
            "Opus" or "Vorbis" => "audio/ogg",
            "AAC" => "audio/aac",
            _ => "audio/mp3"
        };
        Response.ContentType = type;

        if (!CachedEncoders.TryGetValue((codec, bitrate, id), out var encoder))
        {
            encoder = new FFmpegEncoder();
            var search = await AudioManager.SearchID(id);
            if (search == Status.Error) return NotFound();
        
            var result = search.GetOK();
        
            var content_downloader_request = await AudioManager.GetContentDownloader(result);
            if (content_downloader_request == Status.Error) 
                return StatusCode(500);

            var ffmpeg_codec = codec switch
            {
                "Vorbis" => "-c:a libvorbis",
                "AAC" => "-c:a aac",
                "MP3" => "-c:a libmp3lame",
                _ => "-c:a libopus"
            };
            var source_stream_spreader = content_downloader_request.GetOK();
            var stream_subscriber_result = encoder.Convert(bitrate, ffmpeg_codec);
            
            if (stream_subscriber_result == Status.Error) return StatusCode(500);

            var source_stream_subscriber = stream_subscriber_result.GetOK();
            source_stream_spreader.Subscribe(source_stream_subscriber);
        }

        var cache = new ConcurrentQueue<(byte[], int, int)>();
        var waiting_semaphore = new SemaphoreSlim(0, 1);
        var sync_semaphore = new SemaphoreSlim(1, 1);
        var encoded_stream_spreader = encoder.GetStreamSpreader();
        
        var split_query = id.Split("://");
        var pure_id = split_query.Length > 1 ? 
            string.Join("://", split_query[1..]) : split_query[0];
        
        Response.Headers.Append("Content-Disposition", $"attachment; filename={pure_id}");
        Response.Headers.Append("Cache-Control", "no-cache; no-store; must-revalidate");
        
        var stream_subscriber = new StreamSubscriber
        {
            WriteCall = (bytes, offset, length) =>
            {
                cache.Enqueue((bytes, offset, length));
                return HttpContext.RequestAborted.IsCancellationRequested ? 
                    StreamStatus.Closed : StreamStatus.Open;
            },
            SyncCall = SyncCall,
            CloseCall = () =>
            {
                waiting_semaphore.Release();
            }
        };
        encoded_stream_spreader.Subscribe(stream_subscriber);

        await waiting_semaphore.WaitAsync();
        await Response.Body.FlushAsync();
        return new EmptyResult();

        async void SyncCall()
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
}