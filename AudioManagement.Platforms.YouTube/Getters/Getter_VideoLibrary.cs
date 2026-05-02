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
                return Result<StreamSpreader, DownloadError>
                    .Error(DownloadError.NotFound);

            var streamSpreader = new StreamSpreader();

            _ = Task.Run(async () =>
            {
                var stream = await bestAudio.StreamAsync();
                await stream.CopyToAsync(streamSpreader, cancellationToken);
                await streamSpreader.CloseAsync();
            }, cancellationToken);

            return Result<StreamSpreader, DownloadError>.Success(streamSpreader);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return Result<StreamSpreader, DownloadError>.Error(DownloadError.Generic);
        }
    }
}