using Gaida.Core.Platforms;
using Gaida.Platforms.MusicDatabase;
using Gaida.Platforms.YouTube;
using Microsoft.AspNetCore.Mvc;
using Result.Objects;

namespace AudioAPI.Controllers;

public class Artist : ControllerBase
{
    [HttpGet]
    [Route("/Audio/Artist/Local")]
    public async IAsyncEnumerable<PlatformResult> GetArtistLocal(string term,
        [FromServices] ManagerService managerService)
    {
        var platform = managerService.Manager.GetPlatform<MusicDatabase>();
        var songs = await platform.GetArtistSongs(term);

        if (songs == Status.Error)
            yield break;

        foreach (var result in songs.GetOk()) yield return result;
    }

    [HttpGet]
    [Route("/Audio/Artist/YouTube")]
    public async IAsyncEnumerable<PlatformResult> GetArtistYouTube(string term,
        [FromServices] ManagerService managerService)
    {
        var platform = managerService.Manager.GetPlatform<YouTube>();
        var results = await platform.TrySearchKeywords(term);
        if (results == Status.Error)
            yield break;

        foreach (var result in results.GetOk()) yield return result;
    }

    [HttpGet]
    [Route("/Audio/Artist/Info")]
    public async Task<IActionResult> GetArtistInfo(string term)
    {
        return Content("TODO: To be implemented.", "text/plain");
    }
}