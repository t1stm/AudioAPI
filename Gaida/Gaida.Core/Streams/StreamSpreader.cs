namespace Gaida.Core.Streams;

// allocation king, gc go brrrr
public class StreamSpreader : Stream
{
    protected readonly List<(byte[], int, int)> Data = [];
    private readonly TaskCompletionSource ClosedCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected readonly Queue<StreamSubscriber> RemoveQueue = new();
    protected readonly SemaphoreSlim Semaphore = new(1, 1);
    protected readonly List<StreamSubscriber> Subscribers = [];
    public bool Closed { get; protected set; }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => Position;
    public override long Position { get; set; }

    public async Task SubscribeAsync(StreamSubscriber subscriber)
    {
        try
        {
            await Semaphore.WaitAsync();
            Subscribers.Add(subscriber);
            await SyncSubscribers();
        }
        finally
        {
            Semaphore.Release();
        }
    }

    /// <summary>Marks the end of the source data. Not to be confused with <see cref="Stream.Close" />, which disposes.</summary>
    public async Task CloseAsync()
    {
        try
        {
            await Semaphore.WaitAsync();
            Closed = true;
            await SyncSubscribers();
            ClosedCompletion.TrySetResult();
        }
        finally
        {
            Semaphore.Release();
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        try
        {
            Semaphore.Wait();
            Data.Add((buffer.ToArray(), offset, count));
            Position += count;
            SyncSubscribers().GetAwaiter().GetResult();
        }
        finally
        {
            Semaphore.Release();
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Semaphore.WaitAsync(cancellationToken);
            var newArray = new byte[buffer.Length];
            buffer.CopyTo(newArray);
            Data.Add((newArray, 0, newArray.Length));
            Position += newArray.Length;
            await SyncSubscribers();
        }
        finally
        {
            Semaphore.Release();
        }
    }

    /// <summary>Waits until no further bytes can be written to this spreader.</summary>
    public Task WaitForCloseAsync(CancellationToken cancellationToken = default)
    {
        return Closed ? Task.CompletedTask : ClosedCompletion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Returns the completed cached stream, used to satisfy HTTP byte-range requests.</summary>
    public async Task<byte[]> GetBufferedBytesAsync(CancellationToken cancellationToken = default)
    {
        await Semaphore.WaitAsync(cancellationToken);
        try
        {
            await using var output = new MemoryStream();
            foreach (var (bytes, offset, length) in Data)
                await output.WriteAsync(bytes.AsMemory(offset, length), cancellationToken);
            return output.ToArray();
        }
        finally
        {
            Semaphore.Release();
        }
    }

    protected async Task SyncSubscribers()
    {
        while (RemoveQueue.TryDequeue(out var subscriber)) Subscribers.Remove(subscriber);

        foreach (var subscriber in Subscribers)
        {
            var startingIndex = subscriber.CachedDataIndex;
            var dataLength = Data.Count;

            for (subscriber.CachedDataIndex = startingIndex;
                 subscriber.CachedDataIndex < dataLength;
                 subscriber.CachedDataIndex++)
            {
                var currentSlice = Data[subscriber.CachedDataIndex];
                var (bytes, offset, length) = currentSlice;
                var status = await subscriber.WriteCall.Invoke(bytes, offset, length);

                if (!status.HasFlag(StreamStatus.Closed)) continue;

                RemoveQueue.Enqueue(subscriber);
                break;
            }

            _ = Task.Run(async () =>
            {
                await subscriber.SyncCall();
                if (!Closed) return;

                subscriber.SourceClosed = true;
                await subscriber.CloseCall();
            });
        }
    }

    protected override void Dispose(bool disposing)
    {
        Data.Clear();
        base.Dispose(disposing);
    }

    #region Not Supported

    public override void Flush()
    {
        throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    #endregion
}
