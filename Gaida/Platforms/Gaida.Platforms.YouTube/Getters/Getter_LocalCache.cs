using Gaida.Core.Platforms.Errors;
using Gaida.Core.Streams;
using Result;
using Serilog;
using Gaida.Core.Platforms;

namespace Gaida.Platforms.YouTube.Getters;

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
        {
            Logger.Debug("Result is not YouTubeResult, returning error");
            return Task.FromResult(Result<StreamSpreader, DownloadError>.Error(DownloadError.WrongType));
        }

        var file = youtubeResult.GetPureID().ToString() + ".webm";
        Directory.CreateDirectory(CacheLocation);

        var path = Path.Combine(CacheLocation, file);
        if (!File.Exists(path))
        {
            Logger.Debug("File not found, returning error");
            return Task.FromResult(Result<StreamSpreader, DownloadError>.Error(
                DownloadError.FileReadFailure));
        };

        var streamSpreader = new StreamSpreader();
        _ = Task.Run(async () =>
        {
            try
            {
                await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                await stream.CopyToAsync(streamSpreader, cancellationToken);
                await streamSpreader.CloseAsync();
            }
            catch (Exception e)
            {
                Logger.Fatal("Error while copying local cache to StreamSpreader: '{@Exception}'", e);
            }
        }, cancellationToken);

        return Task.FromResult(Result<StreamSpreader, DownloadError>.Success(streamSpreader));
    }
}