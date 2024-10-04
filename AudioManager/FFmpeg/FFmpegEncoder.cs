using System.Collections.Concurrent;
using System.Diagnostics;
using AudioManager.Streams;
using Result;

namespace Audio.FFmpeg;

public class FFmpegEncoder
{
    protected readonly StreamSpreader InnerStreamSpreader = new();
    protected Process? Process;
    
    public Result<StreamSubscriber, FFmpegError> Convert(int bitrate, string codec = "-c:a libopus",
        string output_format = "-f mka")
    {
        var queue = new ConcurrentQueue<(byte[], int, int)>();
        var update_semaphore = new SemaphoreSlim(1, 1);
        
        var process_start_info = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-v quiet -nostats -i - {codec} -b:a {bitrate}k -vn -d copy {output_format} pipe:1",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false
        };
        
        Process = Process.Start(process_start_info);
        if (Process == null) return Result<StreamSubscriber, FFmpegError>
            .Error(FFmpegError.UnableToOpen);

        var stream_subscriber = new StreamSubscriber
        {
            WriteCall = (bytes,offset, length) =>
            {
                queue.Enqueue((bytes,offset, length));
                return StreamStatus.Open;
            },
            SyncCall = () => _ = SyncCall(),
            CloseCall = CloseCall
        };

        Task.Run(async () =>
        {
            await Process.StandardOutput.BaseStream.CopyToAsync(InnerStreamSpreader);
            await InnerStreamSpreader.CloseAsync();
        });
        
        return Result<StreamSubscriber, FFmpegError>.Success(
            stream_subscriber);

        async void CloseCall()
        {
            await SyncCall();
            Process.StandardInput.BaseStream.Close();
        }

        async Task SyncCall()
        {
            await update_semaphore.WaitAsync();

            while (queue.TryDequeue(out var entry))
            {
                var (bytes, offset, length) = entry;
                await Process.StandardInput.BaseStream.WriteAsync(
                    bytes.AsMemory(offset, length));
            }

            update_semaphore.Release();
        }
    }
    
    public StreamSpreader GetStreamSpreader() => InnerStreamSpreader;

    public void Cleanup()
    {
        InnerStreamSpreader.Clean();
        Process?.Close();
    }
}