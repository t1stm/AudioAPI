using AudioManagement.Platforms.Errors;
using AudioManagement.Platforms.YouTube.Search_Providers;
using AudioManagement.Streams;
using Result;
using Serilog;
using YoutubeExplode;

namespace AudioManagement.Platforms.YouTube.Getters;

public class GetterYouTubeExplode(ILogger logger) : ContentGetter(logger)
{
    public override int Priority => 40;
    protected static YoutubeClient Client => YouTubeSearchProviderExplode.Client;

    public override async Task<Result<StreamSpreader, DownloadError>> TryGetContentData(PlatformResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            var youtubeResult = (YouTubeResult)result;

            var youtubeClient = Client;
            var stream = await youtubeClient.Videos.Streams.GetManifestAsync(
                youtubeResult.GetPureID().ToString(), cancellationToken);
            var audioOnlyStreams = stream.GetAudioOnlyStreams()
                .OrderByDescending(s => s.Bitrate.KiloBitsPerSecond * (s.AudioCodec is "Opus" ? 2 : 1));

            var chosenAudioOnlyStream = audioOnlyStreams.FirstOrDefault();
            if (chosenAudioOnlyStream is null)
            {
                Logger.Error("Failed to find audio-only stream for YouTube video ID: {ID}", youtubeResult.ID);
                return Result<StreamSpreader, DownloadError>.Error(DownloadError.NotFound);
            }

            var streamSpreader = new StreamSpreader();
            _ = Task.Run(DownloadFunction, cancellationToken);
            return Result<StreamSpreader, DownloadError>.Success(streamSpreader);

            async Task DownloadFunction()
            {
                try
                {
                    await youtubeClient.Videos.Streams.CopyToAsync(
                        chosenAudioOnlyStream, streamSpreader,
                        cancellationToken: cancellationToken);
                    Logger.Debug("Successfully downloaded audio-only stream for YouTube video ID: {ID}", youtubeResult.ID);
                }
                catch (Exception e)
                {
                    Logger.Fatal("Exception thrown when copying stream to StreamSpreader for YouTube video ID: {ID}, {@Exception}", youtubeResult.ID, e);
                }
                finally
                {
                    await streamSpreader.CloseAsync();
                }
            }
        }
        catch (Exception e)
        {
            Logger.Fatal("Error while processing YouTube: '{@Exception}'", e);
            return Result<StreamSpreader, DownloadError>.Error(DownloadError.Generic);
        }
    }
}