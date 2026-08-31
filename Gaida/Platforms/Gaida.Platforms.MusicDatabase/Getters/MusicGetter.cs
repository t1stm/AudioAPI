using Gaida.Core.Platforms;
using Gaida.Core.Streams;
using Serilog;

namespace Gaida.Platforms.MusicDatabase.Getters;

public class MusicGetter(ILogger logger) : ContentGetter(logger)
{
    public override int Priority => 99;

    public override Task<StreamSpreader?> GetContentDataAsync(PlatformResult result,
        CancellationToken cancellationToken)
    {
        if (result is not MusicResult localResult)
        {
            Logger.Error("MusicGetter: Wrong result type. Expected MusicResult, got {Type}", result.GetType().Name);
            return Task.FromResult<StreamSpreader?>(null);
        }

        Logger.Debug("MusicGetter: Attempting to get content data for: {Path}", localResult.Path);
        if (!File.Exists(localResult.Path))
        {
            Logger.Error("MusicGetter: File not found at path: {Path}", localResult.Path);
            return Task.FromResult<StreamSpreader?>(null);
        }

        var streamSpreader = new StreamSpreader();
        _ = Task.Run(async () =>
        {
            try
            {
                Logger.Debug("MusicGetter: Opening file: {Path}", localResult.Path);
                await using var stream = File.Open(localResult.Path, FileMode.Open, FileAccess.Read);
                await stream.CopyToAsync(streamSpreader, cancellationToken);
                Logger.Debug("MusicGetter: Finished streaming file: {Path}", localResult.Path);
            }
            catch (Exception e)
            {
                Logger.Error(e, "MusicGetter: Error while streaming file {Path}", localResult.Path);
            }
            finally
            {
                await streamSpreader.CloseAsync();
            }
        }, cancellationToken);

        return Task.FromResult<StreamSpreader?>(streamSpreader);
    }
}
