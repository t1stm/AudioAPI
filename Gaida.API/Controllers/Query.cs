using Gaida.API.Contracts;
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
        [FromServices] ManagerService managerService)
    {
        var parsed = QueryParser.Parse(query);
        if (parsed.Kind == ParsedQueryKind.Invalid)
            return BadRequest(Error("invalid_query", parsed.ErrorMessage!));

        try
        {
            return parsed.Kind switch
            {
                ParsedQueryKind.Search => Ok(new QueryResolutionDto { Kind = "search", Query = parsed.Query }),
                ParsedQueryKind.Local => await ResolveOne("local", parsed.Query, managerService),
                ParsedQueryKind.YouTubeVideo => await ResolveOne("youtubeVideo", parsed.Query, managerService),
                ParsedQueryKind.YouTubePlaylist => await ResolvePlaylist(parsed, managerService),
                _ => BadRequest(Error("invalid_query", "The query is not supported."))
            };
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to resolve query {Query}", parsed.Query);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                Error("resolver_unavailable", "The query resolver is temporarily unavailable."));
        }
    }

    private async Task<ActionResult<QueryResolutionDto>> ResolveOne(string kind, string id, ManagerService managerService)
    {
        var result = await managerService.Manager.SearchID(id, HttpContext.RequestAborted);
        if (result is null) return NotFound(Error("not_found", "No result was found for this ID."));

        var mapped = DiscoveryResultMapper.Map(result, Request, configuration, environment);
        return mapped is null
            ? NotFound(Error("not_found", "No result was found for this ID."))
            : Ok(new QueryResolutionDto { Kind = kind, Query = id, Result = mapped });
    }

    private async Task<ActionResult<QueryResolutionDto>> ResolvePlaylist(ParsedQuery parsed, ManagerService managerService)
    {
        var results = new List<SearchResultDto>();
        await foreach (var result in managerService.Manager.SearchPlaylist(parsed.Query, HttpContext.RequestAborted)
                           .WithCancellation(HttpContext.RequestAborted))
        {
            var mapped = DiscoveryResultMapper.Map(result, Request, configuration, environment);
            if (mapped is not null) results.Add(mapped);
        }

        return Ok(new QueryResolutionDto
        {
            Kind = "youtubePlaylist",
            Query = parsed.Query,
            PlaylistId = parsed.PlaylistId,
            Results = results
        });
    }

    private static ApiErrorBody Error(string code, string message) => new(new ApiError(code, message));
}
