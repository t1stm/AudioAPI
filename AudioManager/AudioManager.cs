using AudioManager.Platforms;
using AudioManager.Platforms.Errors;
using AudioManager.Platforms.YouTube;
using Result;

namespace Audio;

public class AudioManager
{
    protected readonly Dictionary<string, Platform> SearchIDMap = [];
    public List<Platform> Platforms { get; } = [
        new YouTube()
    ];

    public void Initialize()
    {
        Platforms.ForEach(MapPlatformIdentifiers);
        Platforms.ForEach(p => p.Initialize());
    }

    public void RegisterPlatform(Platform platform)
    {
        platform.Initialize();
        Platforms.Add(platform);
        MapPlatformIdentifiers(platform);
    }

    protected void MapPlatformIdentifiers(Platform platform)
    {
        foreach (var identifier in platform.SearchIDIdentifiers)
        {
            SearchIDMap.Add(identifier, platform);
        }
    }

    public Task<Result<PlatformResult, SearchError>> SearchID(string id, CancellationToken cancellation_token = default)
    {
        var split_id = id.Split("://");
        var identifier = split_id[0] + "://";
        var pure_id = split_id.Length > 1 ? string.Join("://", split_id[1..]) : id;
        
        return SearchIDMap.TryGetValue(identifier, out var platform) ? 
            platform.SearchID(pure_id, cancellation_token) :
            Task.FromResult(Result<PlatformResult, SearchError>.Error(SearchError.NotFound));
    }
    
    
}