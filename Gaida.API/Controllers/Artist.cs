using Gaida.API.Contracts;
using Gaida.Core.Platforms;
using Microsoft.AspNetCore.Mvc;

namespace Gaida.API.Controllers;

[ApiController]
[Route("[controller]")]
public class Artist(IConfiguration configuration, IHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    [Route("/Audio/Artist/Local")]
    [Produces("application/json")]
    public async Task<ActionResult<IReadOnlyList<SearchResultDto>>> GetArtistLocal(string? term,
        [FromServices] ManagerService managerService)
    {
        if (string.IsNullOrWhiteSpace(term) || managerService.Manager.PlatformFor("audio://") is not HttpPlatform local)
            return Ok(Array.Empty<SearchResultDto>());

        var results = await MapAndOrder(local.ArtistAsync(term, HttpContext.RequestAborted));
        return Ok(results);
    }

    [HttpGet]
    [Route("/Audio/Artist/YouTube")]
    [Produces("application/json")]
    public async Task<ActionResult<IReadOnlyList<SearchResultDto>>> GetArtistYouTube(string? term,
        [FromServices] ManagerService managerService)
    {
        if (string.IsNullOrWhiteSpace(term) || managerService.Manager.PlatformFor("yt://") is not HttpPlatform youTube)
            return Ok(Array.Empty<SearchResultDto>());

        // Just a keyword search on the YouTube pod: there is no separate "artist" route for it.
        var results = await MapAndOrder(youTube.SearchKeywords(term, HttpContext.RequestAborted));
        return Ok(results);
    }

    private async Task<IReadOnlyList<SearchResultDto>> MapAndOrder(IAsyncEnumerable<PlatformResult> source)
    {
        var results = new List<SearchResultDto>();
        await foreach (var result in source.WithCancellation(HttpContext.RequestAborted))
        {
            var mapped = DiscoveryResultMapper.Map(result, Request, configuration, environment);
            if (mapped is not null) results.Add(mapped);
        }

        return
        [
            .. results.OrderBy(result => result.Artist, StringComparer.OrdinalIgnoreCase)
                .ThenBy(result => result.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(result => result.Id, StringComparer.Ordinal)
        ];
    }
}