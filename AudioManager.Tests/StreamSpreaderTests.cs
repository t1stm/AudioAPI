using System.Collections.Concurrent;
using System.Security.Cryptography;
using AudioManager.Platforms.YouTube;
using Result.Objects;
using Xunit.Abstractions;

namespace AudioManager.Tests;

public class StreamSpreaderTests(ITestOutputHelper output)
{
    [Fact]
    public void CorrectDataOrder()
    {
        var stream_spreader = new StreamSpreader();
        var random_bytes = RandomNumberGenerator.GetBytes(1048576);
        
        output.WriteLine("Set up random bytes.");

        var tuples = new (MemoryStream, SemaphoreSlim)[16];
        for (var i = 0; i < tuples.Length; i++)
        {
            var stream = new MemoryStream();
            var waiting_semaphore = new SemaphoreSlim(0, 1);
            var stream_subscriber = new StreamSubscriber
            {
                WriteCall = async (bytes, offset, length) =>
                {
                    await stream.WriteAsync(bytes.AsMemory(offset, length));
                    await Task.Delay(16);
                    return StreamStatus.Open;
                },
                SyncCall = () => Task.CompletedTask,
                CloseCall = () =>
                {
                    waiting_semaphore.Release();
                    return Task.CompletedTask;
                }
            };
            
            stream_spreader.Subscribe(stream_subscriber);
            tuples[i] = (stream, waiting_semaphore);
        }
        
        output.WriteLine("Set up destinations.");
        
        var memory_stream = new MemoryStream(random_bytes);
        memory_stream.CopyTo(stream_spreader, 1 << 12);
        stream_spreader.Close();
        
        output.WriteLine("Copied and closed stream.");
        
        foreach (var (_, semaphore) in tuples)
        {
            semaphore.Wait();
        }
        
        output.WriteLine("Destinations finished copying.");

        var index = 0;
        foreach (var (stream, _) in tuples)
        {
            var stream_array = stream.ToArray();
            
            Assert.Equal(random_bytes.Length, stream_array.Length);
            Assert.Equal(random_bytes, stream_array);
            
            output.WriteLine($"Stream check [{index++}] is successful.");
        }
    }

    [Fact]
    public async Task TestDownloading()
    {
        const int stream_count = 16;
        output.WriteLine("Starting download test.");
        var audio_manager = new Audio.AudioManager();
        
        audio_manager.Initialize();
        audio_manager.RegisterPlatform<YouTube>();
        
        var found = await audio_manager.SearchID("yt://dQw4w9WgXcQ");
        Assert.True(found == Status.OK, "YouTube search for \'dQw4w9WgXcQ\' failed.");
        
        output.WriteLine("Found YouTube result.");

        var result = found.GetOK();
        var download = await result.TryGetContentData();
        Assert.True(download == Status.OK, "YouTube download failed.");
        
        output.WriteLine("Downloading result.");
        var stream_spreader = download.GetOK();
        
        var tuples = new (MemoryStream, SemaphoreSlim)[stream_count];
        for (var i = 0; i < stream_count; i++)
        {
            var waiting_semaphore = new SemaphoreSlim(0, 1);
            
            var stream = new MemoryStream();
            Assert.False(stream == null, $"Stream {i} is null.");
            
            tuples[i] = (stream, waiting_semaphore);
            
            var local_i = i;
            var data_queue = new ConcurrentQueue<(byte[], int, int)>();
            
            var update_semaphore = new SemaphoreSlim(1, 1);
            var stream_subscriber = new StreamSubscriber
            {
                WriteCall = (bytes, offset, length) =>
                {
                    data_queue.Enqueue((bytes, offset, length));
                    return Task.FromResult(StreamStatus.Open);
                },
                SyncCall = () => Task.CompletedTask,
                CloseCall = async () =>
                {
                    output.WriteLine($"Releasing waiting semaphore for stream [{local_i}].");
                    await SyncCall();
                    waiting_semaphore.Release();
                }
            };
            
            stream_spreader.Subscribe(stream_subscriber);
            continue;

            async Task SyncCall()
            {
                if (data_queue.IsEmpty) return;
                await update_semaphore.WaitAsync();

                while (data_queue.TryDequeue(out var tuple))
                {
                    var (bytes, offset, length) = tuple;
                    
                    await stream.WriteAsync(bytes.AsMemory(offset, length));
                    await Task.Delay(16);
                }
                
                await stream.FlushAsync();
                update_semaphore.Release();
            }
        }
        
        output.WriteLine("Waiting output destinations.");

        foreach (var (stream, semaphore) in tuples)
        {
            Assert.False(stream is null, "Stream is null.");
            Assert.False(semaphore is null, "Semaphore is null.");
            await semaphore.WaitAsync();
        }

        var (first_stream, _) = tuples.First();
        var first_array = first_stream.ToArray();
        
        var index = 0;
        foreach (var (stream, _) in tuples)
        {
            var array = stream.ToArray();
            Assert.Equal(first_array, array);
            output.WriteLine($"Equality check for [{index++}] is successful.");
        }
    }

    [Fact]
    public async Task ClosedCopyTest()
    {
        var stream_spreader = new StreamSpreader();
        var random_bytes = RandomNumberGenerator.GetBytes(4096);

        var memory_stream = new MemoryStream();
        var waiting_semaphore = new SemaphoreSlim(0, 1);
        var stream_subscriber = new StreamSubscriber
        {
            WriteCall = async (bytes, offset, length) =>
            {
                await memory_stream.WriteAsync(bytes.AsMemory(offset, length));
                return StreamStatus.Open;
            },
            SyncCall = () => Task.CompletedTask,
            CloseCall = () =>
            {
                waiting_semaphore.Release();
                return Task.CompletedTask;
            }
        };
        
        await stream_spreader.WriteAsync(random_bytes);
        await stream_spreader.CloseAsync();
        await Task.Delay(2000);
        
        stream_spreader.Subscribe(stream_subscriber);
        await Task.Delay(2000);
        
        Assert.False(memory_stream.ToArray().Length == 0, "No data copied to the MemoryStream.");
    }
}