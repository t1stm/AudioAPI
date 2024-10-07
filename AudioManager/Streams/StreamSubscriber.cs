namespace AudioManager.Streams;

public class StreamSubscriber
{
    public required Func<byte[], int, int, Task<StreamStatus>> WriteCall;
    public required Func<Task> SyncCall;
    public required Func<Task> CloseCall;
    public int CachedDataIndex;
    public bool SourceClosed { get; set; }
}