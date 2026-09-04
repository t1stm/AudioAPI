using Gaida.Core.Utils;

namespace Gaida.Tests;

/// <summary>
///     The two sequence helpers the streaming discovery path rests on. Serialisation itself is ASP.NET's
///     job — an IAsyncEnumerable returned from an endpoint is written and flushed element by element — so
///     what is left to check is the ordering these two promise.
/// </summary>
public class StreamingTests
{
    /// <summary>
    ///     The merge produces exactly the count asked for, splits it near the requested share, and lets one
    ///     source cover for the other when it runs dry — the shuffle-then-backfill behaviour RandomResults had
    ///     when it could still assemble the whole list.
    /// </summary>
    [Fact]
    public async Task RandomMergeKeepsTheCountAndTheShare()
    {
        var firstTotal = 0;
        const int runs = 200;

        for (var run = 0; run < runs; run++)
        {
            var merged = await Collect(Numbered("yt", 4).RandomMerge(4, Numbered("local", 10), 10));

            Assert.Equal(10, merged.Count);
            firstTotal += merged.Count(item => item.StartsWith("yt"));
        }

        // Four of ten from the first source every time, once neither source is short.
        Assert.Equal(4 * runs, firstTotal);

        // A source that yields nothing is simply not drawn from: the other one fills the whole count.
        var backfilled = await Collect(Numbered("yt", 0).RandomMerge(4, Numbered("local", 10), 10));
        Assert.Equal(10, backfilled.Count);
        Assert.All(backfilled, item => Assert.StartsWith("local", item));

        // Neither source can cover it: short is short, not a hang.
        var short_ = await Collect(Numbered("yt", 1).RandomMerge(4, Numbered("local", 2), 10));
        Assert.Equal(3, short_.Count);
    }

    /// <summary>
    ///     Lookups overlap but the order they were asked in survives — a playlist resolved track by track has
    ///     to come back in the playlist's order. Misses drop out instead of leaving holes.
    /// </summary>
    [Fact]
    public async Task SelectParallelOverlapsButKeepsOrder()
    {
        var inFlight = 0;
        var peak = 0;

        var results = await Collect(Enumerable.Range(0, 20).AsAsync()
            .SelectParallel(4, async (number, _) =>
            {
                var running = Interlocked.Increment(ref inFlight);
                peak = Math.Max(peak, running);

                // Later items finish first, so anything that yields on completion would come back reversed.
                await Task.Delay(20 - number);
                Interlocked.Decrement(ref inFlight);

                return number % 5 == 0 ? null : $"#{number}";
            }));

        Assert.Equal(Enumerable.Range(0, 20).Where(number => number % 5 != 0).Select(number => $"#{number}"),
            results);
        Assert.True(peak > 1, "the lookups never overlapped");
        Assert.True(peak <= 4, $"the window grew past its limit: {peak}");
    }

    private static async Task<List<T>> Collect<T>(IAsyncEnumerable<T> source)
    {
        var collected = new List<T>();
        await foreach (var item in source) collected.Add(item);
        return collected;
    }

    private static IAsyncEnumerable<string> Numbered(string prefix, int count)
    {
        return Enumerable.Range(0, count).Select(index => $"{prefix}-{index}").AsAsync();
    }

}
