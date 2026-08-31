using Gaida.Core.Platforms;
using Gaida.Platforms.MusicDatabase;
using Gaida.Platforms.YouTube;
using Microsoft.AspNetCore.Mvc;

namespace Gaida.API.Controllers;

public class Artist : ControllerBase
{
    [HttpGet]
    [Route("/Audio/Artist/Local")]
    [Produces("application/json")]
    public IAsyncEnumerable<PlatformResult> GetArtistLocal(string term,
        [FromServices] ManagerService managerService)
    {
        return managerService.Manager.GetPlatform<MusicDatabase>().GetArtistSongs(term);
    }

    [HttpGet]
    [Route("/Audio/Artist/YouTube")]
    [Produces("application/json")]
    public IAsyncEnumerable<PlatformResult> GetArtistYouTube(string term,
        [FromServices] ManagerService managerService)
    {
        return managerService.Manager.GetPlatform<YouTube>()
            .SearchKeywords(term, HttpContext.RequestAborted);
    }
}
