using AudioManager.Platforms.Errors;
using AudioManager.Streams;
using Result;

namespace AudioManager.Platforms.Cross_Platform;

public class Downloader_YtDLP : ContentDownloader
{
    public override int Priority => 0;
    public override async Task<Result<StreamSpreader, DownloadError>> TryGetContentData(PlatformResult result, CancellationToken cancellation_token)
    {
        throw new NotImplementedException();
    }
}