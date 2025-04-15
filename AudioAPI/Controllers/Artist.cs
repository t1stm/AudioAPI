using Microsoft.AspNetCore.Mvc;

namespace AudioAPI.Controllers;

public class Artist : ControllerBase
{
    [HttpGet]
    [Route("/Audio/Artist/Get")]
    public async Task<IActionResult> GetArtist(string term)
    {
        return Ok();
    }
}