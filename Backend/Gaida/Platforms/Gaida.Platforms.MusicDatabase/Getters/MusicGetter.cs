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

        // The library file is the body. Adopting it copies nothing and allocates nothing: the spreader
        // reads straight out of it, and never writes to or deletes it.
        return Task.FromResult<StreamSpreader?>(StreamSpreader.FromExistingFile(localResult.Path));
    }
}