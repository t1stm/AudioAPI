using Audio;
using Microsoft.AspNetCore.Mvc;
using Result.Objects;

namespace AudioAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class Content : ControllerBase
{
    private readonly ILogger<Content> _logger;
    private readonly Audio.AudioManager _audioManager;

    public Content(ILogger<Content> logger)
    {
        _logger = logger;
        _audioManager = new Audio.AudioManager();
        _audioManager.Initialize();
    }


    public async Task<IActionResult> Search(string query)
    {
        _logger.LogInformation("Searching for {Query}", query);
        
        var query_type = _audioManager.FindQueryType(query);
        switch (query_type)
        {
            case QueryType.ID:
            {
                var split_query = query.Split("://");
                var pure_id = split_query.Length > 1 ? 
                    string.Join("://", split_query[1..]) : split_query[0];
                
                var found = await _audioManager.SearchID(pure_id);
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
        }
        
        
        return new EmptyResult();
    }
}