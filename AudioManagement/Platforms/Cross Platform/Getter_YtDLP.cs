using System.Diagnostics;
using AudioManagement.Platforms.Errors;
using AudioManagement.Streams;
using Result;

namespace AudioManagement.Platforms.Cross_Platform;

public sealed class GetterYtDlp : ContentGetter
{
    public override int Priority => 20;

    public override Task<Result<StreamSpreader, DownloadError>> TryGetContentData(
        PlatformResult youtubeResult, CancellationToken cancellationToken)
    {
        var processInfo = GetProcessStartInfo(youtubeResult);
        var process = Process.Start(processInfo);

        if (process is null) return Task.FromResult(Result<StreamSpreader, DownloadError>.Error(DownloadError.Generic));

        var streamSpreader = new StreamSpreader();
        _ = Task.Run(async () =>
        {
            await process.StandardOutput.BaseStream.CopyToAsync(streamSpreader, cancellationToken);
            await streamSpreader.CloseAsync();
        }, cancellationToken);

        return Task.FromResult(Result<StreamSpreader, DownloadError>.Success(streamSpreader));
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