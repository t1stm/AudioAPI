using System.Collections.Concurrent;
using System.Diagnostics;
using AudioManager.Streams;
using Result;

namespace Audio.FFmpeg;

public class FFmpegEncoder
{
    protected readonly StreamSpreader InnerStreamSpreader = new();
    
    public Result<StreamSubscriber, FFmpegError> Convert(int bitrate, string codec = "-c:a libopus")
    {
        var queue = new ConcurrentQueue<(byte[], int, int)>();
        var update_semaphore = new SemaphoreSlim(0, 1);
        
        var process_start_info = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-i - {codec} -b:a {bitrate} -vn -d copy",
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        };
        
        var process = Process.Start(process_start_info);
        if (process == null) return Result<StreamSubscriber, FFmpegError>
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
            await process.StandardOutput.BaseStream.CopyToAsync(InnerStreamSpreader);
            await InnerStreamSpreader.CloseAsync();
        });
        
        return Result<StreamSubscriber, FFmpegError>.Success(
            stream_subscriber);

        async void CloseCall()
        {
            await SyncCall();
            process.StandardInput.BaseStream.Close();
        }

        async Task SyncCall()
        {
            await update_semaphore.WaitAsync();

            while (queue.TryDequeue(out var entry))
            {
                var (bytes, offset, length) = entry;
                await process.StandardInput.BaseStream.WriteAsync(
                    bytes.AsMemory(offset, length));
            }

            update_semaphore.Release();
        }
    }
    
    public StreamSpreader GetStreamSpreader() => InnerStreamSpreader;
}