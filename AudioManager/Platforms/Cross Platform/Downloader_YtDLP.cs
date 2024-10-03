using System.Diagnostics;
using AudioManager.Platforms.Errors;
using AudioManager.Streams;
using Result;

namespace AudioManager.Platforms.Cross_Platform;

public sealed class Downloader_YtDLP : ContentDownloader
{
    public override int Priority => 0;
    public override Task<Result<StreamSpreader, DownloadError>> TryGetContentData(PlatformResult result, CancellationToken cancellation_token)
    {
        var process_info = GetProcessStartInfo(result);
        var process = Process.Start(process_info);
        
        if (process is null)
        {
            return Task.FromResult(Result<StreamSpreader, DownloadError>.Error(DownloadError.GenericError));
        }
        
        var stream_spreader = new StreamSpreader();
        _ = Task.Run(async () =>
        {
            await process.StandardOutput.BaseStream.CopyToAsync(stream_spreader, cancellation_token);
            await stream_spreader.CloseAsync();
        }, cancellation_token);
        
        return Task.FromResult(Result<StreamSpreader, DownloadError>.Success(stream_spreader));
    }

    private static ProcessStartInfo GetProcessStartInfo(PlatformResult result)
    {
        return new ProcessStartInfo
        {
            FileName = "yt-dlp",
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            Arguments = $"-q --no-warnings -u None -p None -r 4.0M -f bestaudio \"{result.GetDownloadUrl()}\" -o -"
        };
    }
}