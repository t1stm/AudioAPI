using System.Diagnostics;
using AudioManager.Platforms.Errors;
using AudioManager.Streams;
using Result;

namespace AudioManager.Platforms.Cross_Platform;

public sealed class Getter_YtDLP : ContentGetter
{
    public override int Priority => 20;
    public override Task<Result<StreamSpreader, DownloadError>> TryGetContentData(
        PlatformResult youtube_result, CancellationToken cancellation_token)
    {
        var process_info = GetProcessStartInfo(youtube_result);
        var process = Process.Start(process_info);
        
        if (process is null)
        {
            return Task.FromResult(Result<StreamSpreader, DownloadError>.Error(DownloadError.Generic));
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
            Arguments = $"-q --no-warnings -r 4.0M -f bestaudio \"{result.GetDownloadUrl()}\" -o -"
        };
    }
}