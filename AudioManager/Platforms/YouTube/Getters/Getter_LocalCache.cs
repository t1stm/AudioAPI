using AudioManager.Platforms.Errors;
using AudioManager.Streams;
using Result;

namespace AudioManager.Platforms.YouTube.Getters;

public class Getter_LocalCache : ContentGetter
{
    public override int Priority => 99;
    public string CacheLocation = "./YouTube Cache";

    public override void Initialize()
    {
        var env = Environment.GetEnvironmentVariable("YOUTUBE_CACHE", EnvironmentVariableTarget.Process);

        if (env is null)
        {
            Environment.SetEnvironmentVariable("YOUTUBE_CACHE", CacheLocation);
        } 
        CacheLocation = env ?? CacheLocation;
        
        base.Initialize();
    }

    public override Task<Result<StreamSpreader, DownloadError>> TryGetContentData(
        PlatformResult result, CancellationToken cancellation_token)
    {
        if (result is not YouTubeResult youtube_result) 
            return Task.FromResult(Result<StreamSpreader, DownloadError>.Error(DownloadError.GenericError));
        
        Directory.CreateDirectory(CacheLocation);
        var path = Path.Combine(CacheLocation, youtube_result.GetPureID());
        if (!File.Exists(path)) return Task.FromResult(Result<StreamSpreader, DownloadError>.Error(
            DownloadError.GenericError));
            
        var stream_spreader = new StreamSpreader();
        _ = Task.Run(async () =>
        {
            await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            await stream.CopyToAsync(stream_spreader, cancellation_token);
            await stream_spreader.CloseAsync();
        }, cancellation_token);
        
        return Task.FromResult(Result<StreamSpreader, DownloadError>.Success(stream_spreader));
    }
}