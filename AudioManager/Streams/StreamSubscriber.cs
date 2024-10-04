namespace AudioManager.Streams;

public class StreamSubscriber
{
    public required Func<byte[], int, int, StreamStatus> WriteCall;
    public required Action SyncCall;
    public required Action CloseCall;
    public int CachedDataIndex;
    public bool SourceClosed { get; set; }
}