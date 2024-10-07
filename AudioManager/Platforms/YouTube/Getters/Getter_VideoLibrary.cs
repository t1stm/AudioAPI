using AudioManager.Platforms.Errors;
using AudioManager.Streams;
using Result;
using VideoLibrary;

namespace AudioManager.Platforms.YouTube.Getters;

public class Getter_VideoLibrary : ContentGetter
{
    public override int Priority => 0;

    public override async Task<Result<StreamSpreader, DownloadError>> TryGetContentData(PlatformResult result, CancellationToken cancellation_token)
    {
        var client = Client.For(VideoLibrary.YouTube.Default);
        var video = await client.GetAllVideosAsync(result.GetDownloadUrl());

        var best_audio = video
            .OrderByDescending(a=> a.AudioBitrate)
            .ThenBy(a => a.AudioFormat is AudioFormat.Opus)
            .FirstOrDefault();
        
        if (best_audio is null) return Result<StreamSpreader, DownloadError>
            .Error(DownloadError.GenericError);

        var stream_spreader = new StreamSpreader();

        _ = Task.Run(async () =>
        {
            var stream = await best_audio.StreamAsync();
            await stream.CopyToAsync(stream_spreader, cancellation_token);
            await stream_spreader.CloseAsync();
        }, cancellation_token);
        
        return Result<StreamSpreader, DownloadError>.Success(stream_spreader);
    }
}