namespace AudioManager.Streams;

public class StreamSpreader : Stream
{
    protected readonly SemaphoreSlim Semaphore = new(1, 1);
    protected readonly List<StreamSubscriber> Subscribers = [];
    protected readonly Queue<StreamSubscriber> RemoveQueue = new();
    protected readonly List<ReadOnlyMemory<byte>> DataQueue = [];
    protected bool Closed;

    public void Subscribe(StreamSubscriber subscriber)
    {
        try
        {
            Semaphore.Wait();
            Subscribers.Add(subscriber);
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
            SyncSubscribers();
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
            SyncSubscribers();
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
            DataQueue.Add(new ReadOnlyMemory<byte>(buffer, offset, count));
            SyncSubscribers();
        }
        finally
        {
            Semaphore.Release();
        }
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        try
        {
            Semaphore.Wait();
            DataQueue.Add(new Memory<byte>(buffer.ToArray()));
            SyncSubscribers();
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
            DataQueue.Add(new ReadOnlyMemory<byte>(buffer, offset, count));
            SyncSubscribers();
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
            DataQueue.Add(buffer);
            SyncSubscribers();
        }
        finally
        {
            Semaphore.Release();
        }
    }

    protected void SyncSubscribers()
    {
        while (RemoveQueue.TryDequeue(out var subscriber))
        {
            Subscribers.Remove(subscriber);
        }
        
        foreach (var subscriber in Subscribers)
        {
            var starting_index = subscriber.CachedDataIndex;
            var data_length = DataQueue.Count;

            for (var i = starting_index; i < data_length; i++)
            {
                var current_slice = DataQueue[i];
                var status = subscriber.WriteCall(current_slice);
                
                if (!status.HasFlag(StreamStatus.Closed)) continue;
                
                RemoveQueue.Enqueue(subscriber);
                break;
            }

            if (Closed)
            {
                subscriber.SourceClosed = true;
            }
            
            subscriber.CachedDataIndex = starting_index;
            Task.Run(subscriber.SyncCall);
        }
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => 0;

    #region Not Supported
    
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    
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