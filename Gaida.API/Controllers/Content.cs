using System.Diagnostics;
using Gaida.API.Contracts;
using Gaida.Core;
using Gaida.Core.FFmpeg;
using Gaida.Core.Platforms;
using Microsoft.AspNetCore.Mvc;

namespace Gaida.API.Controllers;

[ApiController]
[Route("[controller]")]
public class Content(ILogger<Content> logger, IConfiguration configuration, IHostEnvironment environment)
    : ControllerBase
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
            var claim = await manager.ClassifyAsync(query, cancellationToken);
            if (claim.Error is not null)
            {
                // A pod recognised the query as its own but rejected it (e.g. a malformed yt:// id). Discovery
                // stays a valid empty result rather than surfacing the resolver's 400 here — that belongs to
                // /Audio/FindQueryType, which callers use before they commit to a search.
                logger.LogWarning("Classify rejected {Query}: {Error}", query, claim.Error);
                return Ok(results);
            }

            switch (claim.Kind)
            {
                case QueryType.ID:
                {
                    var found = await manager.SearchID(claim.Query, cancellationToken);
                    AddMappedResult(results, found);
                    break;
                }

                case QueryType.Playlist:
                    await AddMappedResults(results, manager.SearchPlaylist(claim.Query, cancellationToken));
                    break;

                case QueryType.Keywords:
                default:
                    await AddMappedResults(results, manager.SearchKeywords(claim.Query, cancellationToken));
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
        [FromServices] ManagerService managerService, int count = 10, double? youTubeShare = null)
    {
        if (count is < 1 or > 200)
            return BadRequest(new ApiErrorBody(new ApiError("invalid_count", "count must be between 1 and 200.")));

        var share = youTubeShare ?? configuration.GetValue("Random:Shares:youtube", 0.4);
        if (share is < 0 or > 1 or double.NaN)
            return BadRequest(new ApiErrorBody(new ApiError("invalid_share", "youTubeShare must be between 0 and 1.")));

        logger.LogInformation("Returning {Count} random results with a {Share} YouTube share", count, share);
        var manager = managerService.Manager;
        var results = new List<SearchResultDto>();

        // Randomized rounding preserves the requested share over time while allowing either source
        // to be selected for small requests (for example, count=1 chooses YouTube 40% of the time).
        var exactYouTubeCount = count * share;
        var youTubeCount = (int)Math.Floor(exactYouTubeCount);
        if (Random.Shared.NextDouble() < exactYouTubeCount - youTubeCount)
            youTubeCount++;

        if (manager.PlatformFor("yt://") is HttpPlatform youTube)
            await AddMappedResults(results, youTube.GetRandomResults(youTubeCount, HttpContext.RequestAborted));

        // Local backfills whatever the YouTube cache was short of, so the caller always gets `count` results.
        if (manager.PlatformFor("audio://") is HttpPlatform local)
            await AddMappedResults(results, local.GetRandomResults(count - results.Count, HttpContext.RequestAborted));

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
        var platform = PlatformFor(managerService.Manager, id);
        if (platform is null) return NotFound();

        using var upstream = await platform.GetContentResponseAsync(id, HttpContext.RequestAborted);
        if (upstream is null) return NotFound();

        RelayContentHeaders(upstream);
        SetCacheHeaders($"raw-{FileId(id)}");

        await upstream.Content.CopyToAsync(Response.Body, HttpContext.RequestAborted);

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

        var platform = PlatformFor(managerService.Manager, id);
        if (platform is null) return NotFound("Search resulted in error");

        using var upstream = await platform.GetContentResponseAsync(id, HttpContext.RequestAborted);
        if (upstream is null) return NotFound("Search resulted in error");

        var fileId = FileId(id);
        Response.ContentType = contentType;
        Response.Headers.Append("Content-Disposition", $"inline; filename={fileId}.{ffmpegOutputFormat[3..]}");
        SetCacheHeaders($"{contentType}-{bitrate}-{fileId}");

        await using var source = await upstream.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
        var ffmpegArguments = $"{ffmpegCodec} -b:a {bitrate}k -vn -d copy {ffmpegOutputFormat}";
        await FFmpegEncoder.EncodeAsync(source, Response.Body, ffmpegArguments, HttpContext.RequestAborted);
        return new EmptyResult();
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

    /// <summary>The platform pod that owns <paramref name="id" />'s protocol prefix, or <c>null</c> when none does.</summary>
    private static HttpPlatform? PlatformFor(AudioManager manager, string id)
    {
        var separator = id.IndexOf("://", StringComparison.Ordinal);
        if (separator < 1) return null;
        return manager.PlatformFor(id[..(separator + 3)]) as HttpPlatform;
    }

    /// <summary>The ID without its platform protocol, safe to put in a header.</summary>
    private static string FileId(string id)
    {
        var separator = id.IndexOf("://", StringComparison.Ordinal);
        var value = separator >= 0 ? id[(separator + 3)..] : id;
        return Uri.EscapeDataString(value);
    }

    private void RelayContentHeaders(HttpResponseMessage upstream)
    {
        Response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        if (upstream.Content.Headers.ContentDisposition is { } disposition)
            Response.Headers.ContentDisposition = disposition.ToString();
    }

    private void SetCacheHeaders(string etag)
    {
        Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
        Response.Headers.ETag = $"\"{etag}\"";
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
}