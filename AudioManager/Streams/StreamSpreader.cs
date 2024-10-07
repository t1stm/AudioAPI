namespace AudioManager.Streams;

public class StreamSpreader : Stream
{
    protected readonly SemaphoreSlim Semaphore = new(1, 1);
    protected readonly List<StreamSubscriber> Subscribers = [];
    protected readonly Queue<StreamSubscriber> RemoveQueue = new();
    protected readonly List<(byte[], int, int)> Data = [];
    public bool Closed { get; protected set; }

    public void Subscribe(StreamSubscriber subscriber)
    {
        try
        {
            Semaphore.Wait();
            Subscribers.Add(subscriber);
            SyncSubscribers().GetAwaiter().GetResult();
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
            SyncSubscribers().GetAwaiter().GetResult();
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

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellation_token)
    {
        try
        {
            await Semaphore.WaitAsync(cancellation_token);
            Data.Add((buffer.ToArray(), offset, count));
            await SyncSubscribers();
        }
        finally
        {
            Semaphore.Release();
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellation_token = default)
    {
        try
        {
            await Semaphore.WaitAsync(cancellation_token);
            var new_array = new byte[buffer.Length];
            buffer.CopyTo(new_array);
            Data.Add((new_array, 0, new_array.Length));
            await SyncSubscribers();
        }
        finally
        {
            Semaphore.Release();
        }
    }

    protected async Task SyncSubscribers()
    {
        while (RemoveQueue.TryDequeue(out var subscriber))
        {
            Subscribers.Remove(subscriber);
        }
        
        foreach (var subscriber in Subscribers)
        {
            var starting_index = subscriber.CachedDataIndex;
            var data_length = Data.Count;

            for (subscriber.CachedDataIndex = starting_index; subscriber.CachedDataIndex < data_length; subscriber.CachedDataIndex++)
            {
                var current_slice = Data[subscriber.CachedDataIndex];
                var (bytes, offset, length) = current_slice;
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

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => Position;
    public override long Position { get; set; }

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