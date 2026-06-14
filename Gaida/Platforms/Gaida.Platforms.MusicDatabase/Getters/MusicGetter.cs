using Gaida.Core.Platforms.Errors;
using Gaida.Core.Streams;
using Result;
using Serilog;
using Gaida.Core.Platforms;

namespace Gaida.Platforms.MusicDatabase.Getters;

public class MusicGetter(ILogger logger) : ContentGetter(logger)
{
    public override int Priority => 99;

    public override Task<Result<StreamSpreader, DownloadError>> TryGetContentData(
        PlatformResult result, CancellationToken cancellationToken)
    {
        if (result is not MusicResult localResult)
        {
            Logger.Error("MusicGetter: Wrong result type. Expected MusicResult, got {Type}", result.GetType().Name);
            return Task.FromResult(Result<StreamSpreader, DownloadError>.Error(DownloadError.WrongType));
        }

        Logger.Debug("MusicGetter: Attempting to get content data for: {Path}", localResult.Path);
        if (!File.Exists(localResult.Path))
        {
            Logger.Error("MusicGetter: File not found at path: {Path}", localResult.Path);
            return Task.FromResult(Result<StreamSpreader, DownloadError>.Error(
                DownloadError.FileReadFailure));
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
                Logger.Error("MusicGetter: Error while streaming file {Path}: {@Exception}", localResult.Path, e);
            }
            finally
            {
                await streamSpreader.CloseAsync();
            }
        }, cancellationToken);

        return Task.FromResult(Result<StreamSpreader, DownloadError>.Success(streamSpreader));
    }
}