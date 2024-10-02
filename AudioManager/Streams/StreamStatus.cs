namespace AudioManager.Streams;

[Flags]
public enum StreamStatus
{
    Open = 1,
    Closed = 1 << 1,
    Error = 1 << 1 | 1 << 2
}