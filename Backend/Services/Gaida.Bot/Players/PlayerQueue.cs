using System.Diagnostics;
using Gaida.Core.Platforms;

namespace Gaida.Bot.Players;

public class PlayerQueue
{
    public required Action<PlatformResult> OnCurrentChange { get; init; }
    public List<PlatformResult> Results { get; } = [];
    
    public int? Index { get; set; }
    public PlatformResult? Current
    {
        get;
        set
        {
            field = value;
            if (value is null) return;
            OnCurrentChange(value);
        }
    }

    public bool IsPlaying { get; set; }
    public long? StartTimestamp { get; set; }
    
    public TimeSpan CurrentTime() => StartTimestamp.HasValue ? Stopwatch.GetElapsedTime(StartTimestamp.Value) : TimeSpan.Zero;
    public TimeSpan Duration { get; set; }
    

    public void Skip(int count = 1)
    {
        Index ??= 0;
        Index += count;
        if (Index >= Results.Count) Index = Results.Count - 1;
        if (Index < 0) Index = Results.Count - 1;
        
        Current = Results[Index.Value];
    }
}