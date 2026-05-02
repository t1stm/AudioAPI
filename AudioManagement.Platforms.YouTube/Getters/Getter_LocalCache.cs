using AudioManagement.Platforms.Errors;
using AudioManagement.Streams;
using Result;
using Serilog;

namespace AudioManagement.Platforms.YouTube.Getters;

public class GetterLocalCache(ILogger logger) : ContentGetter(logger)
{
    public string CacheLocation = "./YouTube Cache";
    public override int Priority => 99;

    public override void Initialize()
    {
        var env = Environment.GetEnvironmentVariable("YOUTUBE_CACHE", EnvironmentVariableTarget.Process);

        if (env is null) Environment.SetEnvironmentVariable("YOUTUBE_CACHE", CacheLocation);
        CacheLocation = env ?? CacheLocation;

        base.Initialize();
    }

    public override Task<Result<StreamSpreader, DownloadError>> TryGetContentData(
        PlatformResult result, CancellationToken cancellationToken)
    {
        if (result is not YouTubeResult youtubeResult)
            return Task.FromResult(Result<StreamSpreader, DownloadError>.Error(DownloadError.WrongType));

        var file = youtubeResult.GetPureID().ToString() + ".webm";
        Directory.CreateDirectory(CacheLocation);

        var path = Path.Combine(CacheLocation, file);
        if (!File.Exists(path))
            return Task.FromResult(Result<StreamSpreader, DownloadError>.Error(
                DownloadError.FileReadFailure));

        var streamSpreader = new StreamSpreader();
        _ = Task.Run(async () =>
        {
            await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            await stream.CopyToAsync(streamSpreader, cancellationToken);
            await streamSpreader.CloseAsync();
        }, cancellationToken);

        return Task.FromResult(Result<StreamSpreader, DownloadError>.Success(streamSpreader));
    }
}