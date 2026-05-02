using System.Collections.Concurrent;
using System.Security.Cryptography;
using AudioManagement.Platforms.YouTube;
using Result.Objects;
using Serilog.Core;
using Xunit.Abstractions;

namespace AudioManagement.Tests;

public class StreamSpreaderTests(ITestOutputHelper output)
{
    [Fact]
    public void CorrectDataOrder()
    {
        var streamSpreader = new StreamSpreader();
        var randomBytes = RandomNumberGenerator.GetBytes(1048576);

        output.WriteLine("Set up random bytes.");

        var tuples = new (MemoryStream, SemaphoreSlim)[16];
        for (var i = 0; i < tuples.Length; i++)
        {
            var stream = new MemoryStream();
            var waitingSemaphore = new SemaphoreSlim(0, 1);
            var streamSubscriber = new StreamSubscriber
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
                    waitingSemaphore.Release();
                    return Task.CompletedTask;
                }
            };

            streamSpreader.Subscribe(streamSubscriber);
            tuples[i] = (stream, waitingSemaphore);
        }

        output.WriteLine("Set up destinations.");

        var memoryStream = new MemoryStream(randomBytes);
        memoryStream.CopyTo(streamSpreader, 1 << 12);
        streamSpreader.Close();

        output.WriteLine("Copied and closed stream.");

        foreach (var (_, semaphore) in tuples) semaphore.Wait();

        output.WriteLine("Destinations finished copying.");

        var index = 0;
        foreach (var (stream, _) in tuples)
        {
            var streamArray = stream.ToArray();

            Assert.Equal(randomBytes.Length, streamArray.Length);
            Assert.Equal(randomBytes, streamArray);

            output.WriteLine($"Stream check [{index++}] is successful.");
        }
    }

    [Fact]
    public async Task TestDownloading()
    {
        const int streamCount = 16;
        output.WriteLine("Starting download test.");
        var audioManager = new AudioManager(Logger.None);

        audioManager.Initialize();
        audioManager.RegisterPlatform<YouTube>();

        var found = await audioManager.SearchID("yt://dQw4w9WgXcQ");
        Assert.True(found == Status.Ok, "YouTube search for \'dQw4w9WgXcQ\' failed.");

        output.WriteLine("Found YouTube result.");

        var result = found.GetOk();
        var download = await result.TryGetContentData();
        Assert.True(download == Status.Ok, "YouTube download failed.");

        output.WriteLine("Downloading result.");
        var streamSpreader = download.GetOk();

        var tuples = new (MemoryStream, SemaphoreSlim)[streamCount];
        for (var i = 0; i < streamCount; i++)
        {
            var waitingSemaphore = new SemaphoreSlim(0, 1);

            var stream = new MemoryStream();
            Assert.False(stream == null, $"Stream {i} is null.");

            tuples[i] = (stream, waitingSemaphore);

            var localI = i;
            var dataQueue = new ConcurrentQueue<(byte[], int, int)>();

            var updateSemaphore = new SemaphoreSlim(1, 1);
            var streamSubscriber = new StreamSubscriber
            {
                WriteCall = (bytes, offset, length) =>
                {
                    dataQueue.Enqueue((bytes, offset, length));
                    return Task.FromResult(StreamStatus.Open);
                },
                SyncCall = () => Task.CompletedTask,
                CloseCall = async () =>
                {
                    output.WriteLine($"Releasing waiting semaphore for stream [{localI}].");
                    await SyncCall();
                    waitingSemaphore.Release();
                }
            };

            streamSpreader.Subscribe(streamSubscriber);
            continue;

            async Task SyncCall()
            {
                if (dataQueue.IsEmpty) return;
                await updateSemaphore.WaitAsync();

                while (dataQueue.TryDequeue(out var tuple))
                {
                    var (bytes, offset, length) = tuple;

                    await stream.WriteAsync(bytes.AsMemory(offset, length));
                    await Task.Delay(16);
                }

                await stream.FlushAsync();
                updateSemaphore.Release();
            }
        }

        output.WriteLine("Waiting output destinations.");

        foreach (var (stream, semaphore) in tuples)
        {
            Assert.False(stream is null, "Stream is null.");
            Assert.False(semaphore is null, "Semaphore is null.");
            await semaphore.WaitAsync();
        }

        var (firstStream, _) = tuples.First();
        var firstArray = firstStream.ToArray();

        var index = 0;
        foreach (var (stream, _) in tuples)
        {
            var array = stream.ToArray();
            Assert.Equal(firstArray, array);
            output.WriteLine($"Equality check for [{index++}] is successful.");
        }
    }

    [Fact]
    public async Task ClosedCopyTest()
    {
        var streamSpreader = new StreamSpreader();
        var randomBytes = RandomNumberGenerator.GetBytes(4096);

        var memoryStream = new MemoryStream();
        var waitingSemaphore = new SemaphoreSlim(0, 1);
        var streamSubscriber = new StreamSubscriber
        {
            WriteCall = async (bytes, offset, length) =>
            {
                await memoryStream.WriteAsync(bytes.AsMemory(offset, length));
                return StreamStatus.Open;
            },
            SyncCall = () => Task.CompletedTask,
            CloseCall = () =>
            {
                waitingSemaphore.Release();
                return Task.CompletedTask;
            }
        };

        await streamSpreader.WriteAsync(randomBytes);
        await streamSpreader.CloseAsync();
        await Task.Delay(2000);

        await streamSpreader.SubscribeAsync(streamSubscriber);
        await Task.Delay(2000);

        Assert.False(memoryStream.ToArray().Length == 0, "No data copied to the MemoryStream.");
    }
}