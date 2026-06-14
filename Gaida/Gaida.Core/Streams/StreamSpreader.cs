namespace Gaida.Core.Streams;

// allocation king, gc go brrrr
public class StreamSpreader : Stream
{
    protected readonly List<(byte[], int, int)> Data = [];
    protected readonly Queue<StreamSubscriber> RemoveQueue = new();
    protected readonly SemaphoreSlim Semaphore = new(1, 1);
    protected readonly List<StreamSubscriber> Subscribers = [];
    public bool Closed { get; protected set; }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => Position;
    public override long Position { get; set; }

    public void Subscribe(StreamSubscriber subscriber)
    {
        SubscribeAsync(subscriber).GetAwaiter().GetResult();
    }

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

    public override void Close()
    {
        try
        {
            Semaphore.Wait();
            Closed = true;
            SyncSubscribers().GetAwaiter().GetResult();
        }
        finally
        {
            Semaphore.Release();
        }
    }

    public async Task CloseAsync()
    {
        try
        {
            await Semaphore.WaitAsync();
            Closed = true;
            await SyncSubscribers();
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
            SyncSubscribers().GetAwaiter().GetResult();
        }
        finally
        {
            Semaphore.Release();
        }
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        try
        {
            await Semaphore.WaitAsync(cancellationToken);
            Data.Add((buffer.ToArray(), offset, count));
            await SyncSubscribers();
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
            await SyncSubscribers();
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

    public override ValueTask DisposeAsync()
    {
        if (!Closed) return ValueTask.CompletedTask;

        Data.Clear();
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }

    public void Clean()
    {
        Data.Clear();
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