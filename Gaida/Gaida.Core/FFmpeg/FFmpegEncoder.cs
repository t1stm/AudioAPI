using System.Collections.Concurrent;
using System.Diagnostics;
using Gaida.Core.Streams;

namespace Gaida.Core.FFmpeg;

public class FFmpegEncoder
{
    protected readonly StreamSpreader InnerStreamSpreader = new();
    protected Process? Process;

    /// <returns>A subscriber that feeds ffmpeg, or <c>null</c> when ffmpeg couldn't be started.</returns>
    public StreamSubscriber? Convert(int bitrate, string codec = "-c:a libopus", string outputFormat = "-f mka")
    {
        return Convert($"{codec} -b:a {bitrate}k -vn -d copy {outputFormat}");
    }

    /// <returns>A subscriber that feeds ffmpeg, or <c>null</c> when ffmpeg couldn't be started.</returns>
    public StreamSubscriber? Convert(string ffmpegArguments)
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
        if (Process == null) return null;

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

        return streamSubscriber;

        async Task CloseCall()
        {
            try
            {
                await SyncCall();
            }
            finally
            {
                // ffmpeg only flushes its last packets once stdin reaches EOF, so this has to happen
                // even when the drain above failed — otherwise the process sits there holding the tail
                // of the encode and the output stream never closes.
                Process.StandardInput.BaseStream.Close();
            }
        }

        async Task SyncCall()
        {
            await updateSemaphore.WaitAsync();

            try
            {
                // Peek, write, then dequeue: dequeuing first dropped the chunk whenever the write to
                // stdin threw, which puts a silent gap in the middle of what ffmpeg is asked to encode.
                // Single consumer, so nothing else can take the entry between the peek and the dequeue.
                while (queue.TryPeek(out var entry))
                {
                    var (bytes, offset, length) = entry;
                    await Process.StandardInput.BaseStream.WriteAsync(
                        bytes.AsMemory(offset, length));

                    queue.TryDequeue(out _);
                }
            }
            finally
            {
                updateSemaphore.Release();
            }
        }
    }

    public StreamSpreader GetStreamSpreader()
    {
        return InnerStreamSpreader;
    }

    public void Cleanup()
    {
        InnerStreamSpreader.Dispose();
        Process?.Close();
    }
}
