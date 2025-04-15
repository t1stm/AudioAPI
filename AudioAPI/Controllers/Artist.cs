using AudioManager.Platforms;
using AudioManager.Platforms.MusicDatabase;
using AudioManager.Platforms.YouTube;
using Microsoft.AspNetCore.Mvc;
using Result.Objects;

namespace AudioAPI.Controllers;

public class Artist : ControllerBase
{
    [HttpGet]
    [Route("/Audio/Artist/Local")]
    public async IAsyncEnumerable<PlatformResult> GetArtistLocal(string term, [FromServices] ManagerService manager_service)
    {
        var platform = manager_service.AudioManager.GetPlatform<MusicDatabase>();
        var songs = await platform.GetArtistSongs(term);
        
        if (songs == Status.Error)
            yield break;

        foreach (var result in songs.GetOK())
        {
            yield return result;
        }
    }
    
    [HttpGet]
    [Route("/Audio/Artist/YouTube")]
    public async IAsyncEnumerable<PlatformResult> GetArtistYouTube(string term, [FromServices] ManagerService manager_service)
    {
        var platform = manager_service.AudioManager.GetPlatform<YouTube>();
        var results = await platform.TrySearchKeywords(term);
        if (results == Status.Error)
            yield break;

        foreach (var result in results.GetOK())
        {
            yield return result;
        }
    }
    
    [HttpGet]
    [Route("/Audio/Artist/Info")]
    public async Task<IActionResult> GetArtistInfo(string term)
    {
        return Content("TODO: To be implemented.", "text/plain");
    }
}