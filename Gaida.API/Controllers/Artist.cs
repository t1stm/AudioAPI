using Gaida.API.Contracts;
using Gaida.Core.Platforms;
using Gaida.Platforms.MusicDatabase;
using Gaida.Platforms.YouTube;
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
        if (string.IsNullOrWhiteSpace(term)) return Ok(Array.Empty<SearchResultDto>());
        var results = await MapAndOrder(managerService.Manager.GetPlatform<MusicDatabase>().GetArtistSongs(term));
        return Ok(results);
    }

    [HttpGet]
    [Route("/Audio/Artist/YouTube")]
    [Produces("application/json")]
    public async Task<ActionResult<IReadOnlyList<SearchResultDto>>> GetArtistYouTube(string? term,
        [FromServices] ManagerService managerService)
    {
        if (string.IsNullOrWhiteSpace(term)) return Ok(Array.Empty<SearchResultDto>());
        var results = await MapAndOrder(managerService.Manager.GetPlatform<YouTube>()
            .SearchKeywords(term, HttpContext.RequestAborted));
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

        return results.OrderBy(result => result.Artist, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Id, StringComparer.Ordinal)
            .ToArray();
    }
}
