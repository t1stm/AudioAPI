namespace Gaida.Core.Streams;

public class StreamSubscriber
{
    public int CachedDataIndex;
    public required Func<Task> CloseCall;

    /// <summary>Set once this subscriber is gone (client left, or one of its calls threw). Reaped on the next sync.</summary>
    public volatile bool Dead;

    public required Func<Task> SyncCall;
    public required Func<byte[], int, int, Task<StreamStatus>> WriteCall;
    public bool SourceClosed { get; set; }
}
