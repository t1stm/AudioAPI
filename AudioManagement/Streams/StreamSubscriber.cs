namespace AudioManagement.Streams;

public class StreamSubscriber
{
    public int CachedDataIndex;
    public required Func<Task> CloseCall;
    public required Func<Task> SyncCall;
    public required Func<byte[], int, int, Task<StreamStatus>> WriteCall;
    public bool SourceClosed { get; set; }
}