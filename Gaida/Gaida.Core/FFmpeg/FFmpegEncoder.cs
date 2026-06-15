using System.Collections.Concurrent;
using System.Diagnostics;
using Gaida.Core.Streams;
using Result;

namespace Gaida.Core.FFmpeg;

public class FFmpegEncoder
{
    protected readonly StreamSpreader InnerStreamSpreader = new();
    protected Process? Process;

    public Result<StreamSubscriber, FFmpegError> Convert(int bitrate, string codec = "-c:a libopus",
        string outputFormat = "-f mka")
    {
        return Convert($"{codec} -b:a {bitrate}k -vn -d copy {outputFormat}");
    }

    public Result<StreamSubscriber, FFmpegError> Convert(string ffmpegArguments)
    {
        var queue = new ConcurrentQueue<(byte[], int, int)>();
        var updateSemaphore = new SemaphoreSlim(1, 1);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = "-v quiet -nostats -i - " + ffmpegArguments + " pipe:1",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false
        };

        Process = Process.Start(processStartInfo);
        if (Process == null)
            return Result<StreamSubscriber, FFmpegError>
                .Error(FFmpegError.UnableToOpen);

        var streamSubscriber = new StreamSubscriber
        {
            WriteCall = (bytes, offset, length) =>
            {
                queue.Enqueue((bytes, offset, length));
                return Task.FromResult(StreamStatus.Open);
            },
            SyncCall = SyncCall,
            CloseCall = CloseCall
        };

        Task.Run(async () =>
        {
            await Process.StandardOutput.BaseStream.CopyToAsync(InnerStreamSpreader);
            await InnerStreamSpreader.CloseAsync();
        });

        return Result<StreamSubscriber, FFmpegError>.Success(
            streamSubscriber);

        async Task CloseCall()
        {
            await SyncCall();
            Process.StandardInput.BaseStream.Close();
        }

        async Task SyncCall()
        {
            await updateSemaphore.WaitAsync();

            while (queue.TryDequeue(out var entry))
            {
                var (bytes, offset, length) = entry;
                await Process.StandardInput.BaseStream.WriteAsync(
                    bytes.AsMemory(offset, length));
            }

            updateSemaphore.Release();
        }
    }

    public StreamSpreader GetStreamSpreader()
    {
        return InnerStreamSpreader;
    }

    public void Cleanup()
    {
        InnerStreamSpreader.Clean();
        Process?.Close();
    }
}