using AudioManagement.Platforms.Errors;
using AudioManagement.Streams;
using Result;
using Serilog;
using VideoLibrary;

namespace AudioManagement.Platforms.YouTube.Getters;

public class GetterVideoLibrary(ILogger logger) : ContentGetter(logger)
{
    public override int Priority => 0;

    public override async Task<Result<StreamSpreader, DownloadError>> TryGetContentData(PlatformResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = Client.For(VideoLibrary.YouTube.Default);
            var video = await client.GetAllVideosAsync(result.GetDownloadUrl());

            var bestAudio = video
                .OrderByDescending(a => a.AudioBitrate)
                .ThenBy(a => a.AudioFormat is AudioFormat.Opus)
                .FirstOrDefault();

            if (bestAudio is null)
            {
                Logger.Error("Failed to find best audio stream for video library URL: {URL}", result.GetDownloadUrl());
                return Result<StreamSpreader, DownloadError>
                    .Error(DownloadError.NotFound);
            }

            var streamSpreader = new StreamSpreader();

            _ = Task.Run(async () =>
            {
                try
                {
                    var stream = await bestAudio.StreamAsync();
                    await stream.CopyToAsync(streamSpreader, cancellationToken);
                    await streamSpreader.CloseAsync();
                }
                catch (Exception e)
                {
                    Logger.Fatal("Error while copying video library stream to StreamSpreader: '{@Exception}'", e);
                }
            }, cancellationToken);

            return Result<StreamSpreader, DownloadError>.Success(streamSpreader);
        }
        catch (Exception e)
        {
            Logger.Fatal("Error while processing video library: '{@Exception}'", e);
            return Result<StreamSpreader, DownloadError>.Error(DownloadError.Generic);
        }
    }
}