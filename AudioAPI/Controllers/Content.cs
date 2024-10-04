using System.Collections.Concurrent;
using System.Timers;
using Audio;
using Audio.FFmpeg;
using AudioManager.Streams;
using Microsoft.AspNetCore.Mvc;
using Result.Objects;
using WebApplication3;

namespace AudioAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class Content(ILogger<Content> logger) : ControllerBase
{
    private readonly ILogger<Content> Logger = logger;
    public static Audio.AudioManager AudioManager => Globals.AudioManager;
    public static Dictionary<(string codec, int bitrate, string id), FFmpegEncoder> CachedEncoders => Globals.CachedEncoders;
    public static Dictionary<(string codec, int bitrate, string id), DateTime> ExpireTimes => Globals.ExpireTimes;
    public static SemaphoreSlim CacheSemaphore => Globals.CacheSemaphore;

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
        
        var start = DateTime.Now;
        var search = await AudioManager.SearchID(id);
        if (search == Status.Error) return NotFound();
        
        var result = search.GetOK();

        var found_result = DateTime.Now;
        Logger.LogInformation("Searching \'{Id}\' took \'{Duration}\'", id, found_result - start);
        
        var content_downloader_request = 
            await AudioManager.TryGetContentData(result);
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

        var subscribed = DateTime.Now;
        stream_spreader.Subscribe(stream_subscriber);

        await waiting_semaphore.WaitAsync();
        await Response.Body.FlushAsync();
        
        var finish = DateTime.Now;
        Logger.LogInformation(
            "Finishing \'{Id}\' took: \'{Duration}\', with the time while subscribed being \'{Time}\'", 
            id, finish - start, finish - subscribed);
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

        await CacheSemaphore.WaitAsync();
        if (!CachedEncoders.TryGetValue((codec, bitrate, id), out var encoder))
        {
            encoder = new FFmpegEncoder();
            
            var search = await AudioManager.SearchID(id);
            if (search == Status.Error) return NotFound();
        
            var result = search.GetOK();
        
            var content_downloader_request = 
                await AudioManager.TryGetContentData(result);
            
            if (content_downloader_request == Status.Error) 
                return StatusCode(500);
            
            CachedEncoders.Add((codec, bitrate, id), encoder);
            CacheSemaphore.Release();

            var ffmpeg_codec = codec switch
            {
                "Vorbis" => "-c:a libvorbis",
                "AAC" => "-c:a aac",
                "MP3" => "-c:a libmp3lame",
                _ => "-c:a libopus"
            };
            
            var ffmpeg_output_format = codec switch
            {
                "Opus" or "Vorbis" => "-f ogg",
                "AAC" => "-f adts",
                "MP3" => "-f mp3",
                _ => "-f mka"
            };
            
            var source_stream_spreader = content_downloader_request.GetOK();
            var stream_subscriber_result = encoder.Convert(bitrate, ffmpeg_codec, ffmpeg_output_format);
            
            if (stream_subscriber_result == Status.Error) return StatusCode(500);

            var source_stream_subscriber = stream_subscriber_result.GetOK();
            source_stream_spreader.Subscribe(source_stream_subscriber);
        }
        else CacheSemaphore.Release();

        var cache = new ConcurrentQueue<(byte[], int, int)>();
        var waiting_semaphore = new SemaphoreSlim(0, 1);
        var sync_semaphore = new SemaphoreSlim(1, 1);
        var encoder_stream_spreader = encoder.GetStreamSpreader();
        
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
                ExpireTimes.Add((codec, bitrate, id), DateTime.Now.Add(TimeSpan.FromMinutes(45)));
            }
        };
        encoder_stream_spreader.Subscribe(stream_subscriber);

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