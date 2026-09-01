using System.Collections.Concurrent;
using System.Security.Cryptography;
using Gaida.Core;
using Gaida.Platforms.YouTube;
using Serilog.Core;
using Xunit.Abstractions;

namespace Gaida.Tests;

public class StreamSpreaderTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CorrectDataOrder()
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

            await streamSpreader.SubscribeAsync(streamSubscriber);
            tuples[i] = (stream, waitingSemaphore);
        }

        output.WriteLine("Set up destinations.");

        var memoryStream = new MemoryStream(randomBytes);
        memoryStream.CopyTo(streamSpreader, 1 << 12);
        await streamSpreader.CloseAsync();

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

    /// <summary>
    ///     The expiry timer disposes encoders while range requests may still be copying their buffer, so a read is
    ///     either the whole thing or nothing, never a fault mid-enumeration.
    /// </summary>
    [Fact]
    public async Task DisposeDoesNotFaultConcurrentBufferedReads()
    {
        var streamSpreader = new StreamSpreader();
        var randomBytes = RandomNumberGenerator.GetBytes(1 << 20);

        new MemoryStream(randomBytes).CopyTo(streamSpreader, 1 << 12);
        await streamSpreader.CloseAsync();

        var reads = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => streamSpreader.GetBufferedBytesAsync()))
            .ToArray();

        streamSpreader.Dispose();

        foreach (var read in reads)
        {
            var bytes = await read;
            if (bytes.Length == 0) continue;

            Assert.Equal(randomBytes, bytes);
        }
    }

    [Fact]
    public async Task TestDownloading()
    {
        const int streamCount = 16;
        output.WriteLine("Starting download test.");
        var audioManager = new AudioManager(Logger.None);

        audioManager.RegisterPlatform(new YouTube(Logger.None));

        var result = await audioManager.SearchID("yt://dQw4w9WgXcQ");
        Assert.True(result is not null, "YouTube search for \'dQw4w9WgXcQ\' failed.");

        output.WriteLine("Found YouTube result.");

        var streamSpreader = await result!.GetContentDataAsync();
        Assert.True(streamSpreader is not null, "YouTube download failed.");

        output.WriteLine("Downloading result.");

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

            await streamSpreader.SubscribeAsync(streamSubscriber);
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

    /// <summary>
    ///     A subscriber whose client vanished throws on every later write. It must be dropped instead of
    ///     aborting the sync pass, or one abandoned HTTP response truncates the stream for everyone else.
    /// </summary>
    [Fact]
    public async Task FaultingSubscriberDoesNotStarveOthers()
    {
        var streamSpreader = new StreamSpreader();
        var chunks = new[] { RandomNumberGenerator.GetBytes(4096), RandomNumberGenerator.GetBytes(4096) };

        var poisoned = new StreamSubscriber
        {
            WriteCall = (_, _, _) => throw new ObjectDisposedException("IFeatureCollection"),
            SyncCall = () => throw new ObjectDisposedException("IFeatureCollection"),
            CloseCall = () => throw new ObjectDisposedException("IFeatureCollection")
        };

        var healthy = new MemoryStream();
        var finished = new SemaphoreSlim(0, 1);
        var good = new StreamSubscriber
        {
            WriteCall = async (bytes, offset, length) =>
            {
                await healthy.WriteAsync(bytes.AsMemory(offset, length));
                return StreamStatus.Open;
            },
            SyncCall = () => Task.CompletedTask,
            CloseCall = () =>
            {
                finished.Release();
                return Task.CompletedTask;
            }
        };

        // The poisoned subscriber is first in line, so it gets to fail before the healthy one is reached.
        await streamSpreader.SubscribeAsync(poisoned);
        await streamSpreader.SubscribeAsync(good);

        foreach (var chunk in chunks) await streamSpreader.WriteAsync(chunk);
        await streamSpreader.CloseAsync();

        Assert.True(await finished.WaitAsync(TimeSpan.FromSeconds(10)), "Healthy subscriber never completed.");
        Assert.Equal(chunks.SelectMany(chunk => chunk).ToArray(), healthy.ToArray());
    }
}
