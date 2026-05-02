using AudioManagement.Platforms.Errors;
using AudioManagement.Streams;
using Result;
using Serilog;

namespace AudioManagement.Platforms.MusicDatabase.Getters;

public class MusicGetter(ILogger logger) : ContentGetter(logger)
{
    public override int Priority => 99;

    public override Task<Result<StreamSpreader, DownloadError>> TryGetContentData(
        PlatformResult result, CancellationToken cancellationToken)
    {
        if (result is not MusicResult localResult)
            return Task.FromResult(Result<StreamSpreader, DownloadError>.Error(DownloadError.WrongType));

        if (!File.Exists(localResult.Path))
            return Task.FromResult(Result<StreamSpreader, DownloadError>.Error(
                DownloadError.FileReadFailure));

        var streamSpreader = new StreamSpreader();
        _ = Task.Run(async () =>
        {
            await using var stream = File.Open(localResult.Path, FileMode.Open, FileAccess.Read);
            await stream.CopyToAsync(streamSpreader, cancellationToken);
            await streamSpreader.CloseAsync();
        }, cancellationToken);

        return Task.FromResult(Result<StreamSpreader, DownloadError>.Success(streamSpreader));
    }
}