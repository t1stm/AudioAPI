using System.Globalization;
using Gaida.API.Contracts;
using Gaida.Core;
using Gaida.Core.Platforms;
using Microsoft.AspNetCore.Mvc;

namespace Gaida.API.Controllers;

[ApiController]
[Route("[controller]")]
public class Query(ILogger<Query> logger, IConfiguration configuration, IHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    [Route("/Audio/FindQueryType")]
    [Produces("application/json")]
    [ProducesResponseType<QueryResolutionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorBody>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorBody>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QueryResolutionDto>> FindQueryType(string? query,
        [FromServices] ManagerService managerService, [FromServices] PlayableResolver resolver)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(Error("invalid_query", "Query is required."));

        var trimmed = query.Trim();
        try
        {
            // Fanned out across every platform pod's /classify: first claim wins, and nobody claiming means
            // an ordinary keyword search — the one classification rule left in Gaida.
            var claim = await managerService.Manager.ClassifyAsync(trimmed, HttpContext.RequestAborted);
            if (claim.Error is not null) return BadRequest(Error("invalid_query", claim.Error));

            return claim.Kind switch
            {
                QueryType.ID => await ResolveOne(claim.Query, managerService, resolver),
                QueryType.Playlist => await ResolvePlaylist(claim.Query, managerService, resolver),
                _ => Ok(new QueryResolutionDto { Kind = "search", Query = trimmed })
            };
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to resolve query {Query}", trimmed);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                Error("resolver_unavailable", "The query resolver is temporarily unavailable."));
        }
    }

    /// <summary>
    ///     Searches the local library only: the client sends the title rather than an ID, so this touches
    ///     nothing but the local pod's in-memory song list and can run after every roll.
    /// </summary>
    [HttpGet]
    [Route("/Audio/Local/Variant")]
    [Produces("application/json")]
    [ProducesResponseType<LocalVariantDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorBody>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LocalVariantDto>> LocalVariant(string? name, string? artist, string? duration,
        [FromServices] ManagerService managerService)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(Error("invalid_query", "A track name is required."));

        var length = TimeSpan.Zero;
        if (!string.IsNullOrWhiteSpace(duration) &&
            !TimeSpan.TryParse(duration, CultureInfo.InvariantCulture, out length))
            return BadRequest(Error("invalid_query", "The duration must look like 00:04:32."));

        if (managerService.Manager.PlatformFor("audio://") is not HttpPlatform local) return NoContent();

        var found = await local.VariantAsync(name, artist, length, HttpContext.RequestAborted);
        if (found?.Result is null) return NoContent();

        var mapped = ToSearchResult(found.Result);
        if (mapped is null) return NoContent();

        return Ok(new LocalVariantDto(
            found.Match ?? "weak",
            Math.Round(found.Score, 3),
            found.DurationDeltaSeconds,
            found.YouTubeTags ?? [],
            found.LibraryTags ?? [],
            mapped));
    }

    private async Task<ActionResult<QueryResolutionDto>> ResolveOne(string id, ManagerService managerService,
        PlayableResolver resolver)
    {
        var result = await managerService.Manager.SearchID(id, HttpContext.RequestAborted);

        // A Spotify link resolves to a name; what the client gets back is whatever platform actually has the
        // track, which is also what `kind` then reports.
        if (result is not null) result = await resolver.ResolveOne(result, HttpContext.RequestAborted);
        if (result is null) return NotFound(Error("not_found", "No result was found for this ID."));

        var mapped = DiscoveryResultMapper.Map(result, Request, configuration, environment);
        return mapped is null
            ? NotFound(Error("not_found", "No result was found for this ID."))
            : Ok(new QueryResolutionDto { Kind = KindOf(result.ID), Query = id, Result = mapped });
    }

    /// <summary>
    ///     The one discovery response still assembled whole: <c>results</c> lives inside an envelope, and a
    ///     half-written envelope is not something a streaming client can read. Callers that want the entries as
    ///     they arrive should send the canonical <c>query</c> back to <c>/Audio/Search</c>, which routes a
    ///     playlist claim to the same lookup and streams it — see API.md.
    /// </summary>
    private async Task<ActionResult<QueryResolutionDto>> ResolvePlaylist(string playlistUrl,
        ManagerService managerService, PlayableResolver resolver)
    {
        var entries = managerService.Manager.SearchPlaylist(playlistUrl, HttpContext.RequestAborted);
        if (managerService.NeedsResolving(playlistUrl))
            entries = resolver.Resolve(entries, HttpContext.RequestAborted);

        var results = new List<SearchResultDto>();
        await foreach (var result in entries)
        {
            var mapped = DiscoveryResultMapper.Map(result, Request, configuration, environment);
            if (mapped is not null) results.Add(mapped);
        }

        var spotify = playlistUrl.StartsWith("spotify-playlist://", StringComparison.OrdinalIgnoreCase);

        return Ok(new QueryResolutionDto
        {
            Kind = spotify ? "spotifyPlaylist" : "youtubePlaylist",
            Query = playlistUrl,
            PlaylistId = spotify ? playlistUrl["spotify-playlist://".Length..] : ExtractPlaylistId(playlistUrl),
            Results = results
        });
    }

    private SearchResultDto? ToSearchResult(PodResultDto dto)
    {
        // The local pod's own conversion lives on HttpPlatform; this is the one-off case where a
        // sub-resource (the variant's matched track) needs the same treatment without a full search.
        return DiscoveryResultMapper.Map(new HttpResult
        {
            ID = dto.Id ?? "",
            Name = dto.Name,
            Artist = dto.Artist,
            Album = dto.Album,
            Duration = TimeSpan.TryParse(dto.Duration, CultureInfo.InvariantCulture, out var d) ? d : TimeSpan.Zero,
            ThumbnailUrl = dto.ThumbnailUrl,
            OriginalTitle = dto.OriginalTitle,
            OriginalArtist = dto.OriginalArtist,
            Downloaders = []
        }, Request, configuration, environment);
    }

    /// <summary>
    ///     The response's <c>kind</c> for a resolved ID, read off its protocol prefix — display metadata,
    ///     not routing (routing already happened via <c>/classify</c>).
    /// </summary>
    private static string KindOf(string id)
    {
        return id switch
        {
            _ when id.StartsWith("audio://", StringComparison.Ordinal) => "local",
            _ when id.StartsWith("yt://", StringComparison.Ordinal) => "youtubeVideo",
            _ when id.StartsWith("spotify://", StringComparison.Ordinal) => "spotifyTrack",
            _ => "id"
        };
    }

    /// <summary>
    ///     Pulls a YouTube-style <c>list=</c> parameter off a normalized playlist URL, for the
    ///     <c>playlistId</c> field the public contract documents. Best-effort: null when the pod's normalized
    ///     form does not carry one.
    /// </summary>
    private static string? ExtractPlaylistId(string url)
    {
        var index = url.IndexOf("list=", StringComparison.Ordinal);
        if (index < 0) return null;

        var start = index + "list=".Length;
        var end = url.IndexOfAny(['&', '#'], start);
        var value = end < 0 ? url[start..] : url[start..end];
        return Uri.UnescapeDataString(value);
    }

    private static ApiErrorBody Error(string code, string message)
    {
        return new ApiErrorBody(new ApiError(code, message));
    }
}