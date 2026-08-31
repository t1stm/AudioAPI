using System.Diagnostics;
using Gaida.Core.Streams;
using Serilog;

namespace Gaida.Core.Platforms.Cross_Platform;

public sealed class GetterYtDlp(ILogger logger) : ContentGetter(logger)
{
    public override int Priority => 20;

    public override Task<StreamSpreader?> GetContentDataAsync(PlatformResult result,
        CancellationToken cancellationToken)
    {
        var process = Process.Start(GetProcessStartInfo(result));
        if (process is null)
        {
            Logger.Error("Failed to start yt-dlp for URL: {URL}", result.GetDownloadUrl());
            return Task.FromResult<StreamSpreader?>(null);
        }

        var streamSpreader = new StreamSpreader();
        _ = Task.Run(async () =>
        {
            await process.StandardOutput.BaseStream.CopyToAsync(streamSpreader, cancellationToken);
            await streamSpreader.CloseAsync();
        }, cancellationToken);

        return Task.FromResult<StreamSpreader?>(streamSpreader);
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
