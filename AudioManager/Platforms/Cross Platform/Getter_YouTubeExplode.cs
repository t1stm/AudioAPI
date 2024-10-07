using AudioManager.Platforms.Errors;
using AudioManager.Platforms.YouTube;
using AudioManager.Streams;
using Result;
using YoutubeExplode;

namespace AudioManager.Platforms.Cross_Platform;

public class Getter_YouTubeExplode : ContentGetter
{
    public override int Priority => 20;
    protected static YoutubeClient Client => YouTubeSearchProvider_Explode.Client;
    
    public override async Task<Result<StreamSpreader, DownloadError>> TryGetContentData(PlatformResult result, CancellationToken cancellation_token)
    {
        var youtube_result = (YouTubeResult)result;
        
        var youtube_client = Client;
        var stream = await youtube_client.Videos.Streams.GetManifestAsync(
            youtube_result.GetPureID(), cancellation_token);
        var audio_only_streams = stream.GetAudioOnlyStreams()
            .OrderBy(s => s.AudioCodec is "Opus");
        
        var chosen_audio_only_stream = audio_only_streams.FirstOrDefault();
        if (chosen_audio_only_stream is null)
            return Result<StreamSpreader, DownloadError>.Error(DownloadError.GenericError);

        var stream_spreader = new StreamSpreader();
        _ = Task.Run(DownloadFunction, cancellation_token);
        
        return Result<StreamSpreader, DownloadError>.Success(stream_spreader);

        async Task DownloadFunction()
        {
            try
            {
                await youtube_client.Videos.Streams.CopyToAsync(
                    chosen_audio_only_stream, stream_spreader, 
                    cancellationToken: cancellation_token);
            }
            finally
            {
                await stream_spreader.CloseAsync();
            }
        }
    }
}