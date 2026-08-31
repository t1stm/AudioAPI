using Gaida.Core.Platforms;
using Gaida.Core.Streams;
using Serilog;

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

    public override Task<StreamSpreader?> GetContentDataAsync(PlatformResult result,
        CancellationToken cancellationToken)
    {
        if (result is not YouTubeResult youtubeResult)
        {
            Logger.Debug("Result is not a YouTubeResult");
            return Task.FromResult<StreamSpreader?>(null);
        }

        var file = youtubeResult.GetPureID().ToString() + ".webm";
        Directory.CreateDirectory(CacheLocation);

        var path = Path.Combine(CacheLocation, file);
        if (!File.Exists(path))
        {
            Logger.Debug("Not in the local cache: {Path}", path);
            return Task.FromResult<StreamSpreader?>(null);
        }

        var streamSpreader = new StreamSpreader();
        _ = Task.Run(async () =>
        {
            try
            {
                await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                await stream.CopyToAsync(streamSpreader, cancellationToken);
            }
            catch (Exception e)
            {
                Logger.Fatal(e, "Error while copying local cache to StreamSpreader");
            }
            finally
            {
                await streamSpreader.CloseAsync();
            }
        }, cancellationToken);

        return Task.FromResult<StreamSpreader?>(streamSpreader);
    }
}
