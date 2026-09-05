using System.Runtime.CompilerServices;
using Serilog;

namespace Gaida.Core.Utils;

public static class Streaming
{
    /// <summary>
    ///     Draws from two sources at random until <paramref name="total" /> items have been produced, picking
    ///     <paramref name="first" /> with a probability of what it still owes against what is still wanted.
    ///     What a shuffle of the finished list does for a buffered response, without a finished list — the mix
    ///     is the same, it just arrives interleaved. A source that ends early stops being drawn from, so the
    ///     other one backfills it.
    /// </summary>
    /// <remarks>
    ///     ponytail: no prefetch, one item pulled at a time. Both sources here are pods answering from memory,
    ///     so the round trip is already in the noise next to the client's own.
    /// </remarks>
    public static async IAsyncEnumerable<T> RandomMerge<T>(this IAsyncEnumerable<T> first, int firstShare,
        IAsyncEnumerable<T> second, int total,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var firstEnumerator = first.GetAsyncEnumerator(cancellationToken);
        await using var secondEnumerator = second.GetAsyncEnumerator(cancellationToken);

        var owed = firstShare;
        var emitted = 0;
        bool firstDone = false, secondDone = false;

        while (emitted < total && !(firstDone && secondDone))
        {
            var takeFirst = !firstDone && (secondDone || Random.Shared.Next(total - emitted) < owed);
            var enumerator = takeFirst ? firstEnumerator : secondEnumerator;

            if (!await enumerator.MoveNextAsync())
            {
                if (takeFirst)
                {
                    firstDone = true;
                    owed = 0;
                }
                else
                {
                    secondDone = true;
                }

                continue;
            }

            if (takeFirst) owed--;
            emitted++;
            yield return enumerator.Current;
        }
    }

    /// <summary>
    ///     Runs <paramref name="selector" /> over the source with at most <paramref name="concurrency" /> lookups
    ///     in flight, yielding in source order. For turning a list of names into playable results: the searches
    ///     overlap, but a playlist still arrives in the order it was written. Nulls (nothing found) are dropped.
    /// </summary>
    /// <remarks>
    ///     A selector that answers without going anywhere — the resolver handing back a result that is already
    ///     playable — completes synchronously, and the window stops filling the moment one does. That is what
    ///     lets an ordinary keyword search go through the resolver at all: its first hit leaves immediately
    ///     instead of waiting for <paramref name="concurrency" /> results to be pulled behind it, while a
    ///     playlist of names still resolves as many at a time as it is allowed.
    /// </remarks>
    public static async IAsyncEnumerable<TOut> SelectParallel<TIn, TOut>(this IAsyncEnumerable<TIn> source,
        int concurrency, Func<TIn, CancellationToken, Task<TOut?>> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where TOut : class
    {
        var pending = new Queue<Task<TOut?>>(concurrency);
        await using var enumerator = source.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            // Never past `concurrency`, so the window stays a window rather than racing ahead of what the
            // caller is consuming.
            while (pending.Count < concurrency && await enumerator.MoveNextAsync())
            {
                var lookup = selector(enumerator.Current, cancellationToken);
                pending.Enqueue(lookup);
                if (lookup.IsCompleted) break;
            }

            if (pending.Count == 0) yield break;

            var result = await pending.Dequeue();
            if (result is not null) yield return result;
        }
    }

    /// <summary>Adapts an already-materialised sequence to the streaming interfaces.</summary>
#pragma warning disable CS1998 // sequence is already in memory, there is nothing to await
    public static async IAsyncEnumerable<T> AsAsync<T>(this IEnumerable<T> source)
#pragma warning restore CS1998
    {
        foreach (var item in source) yield return item;
    }

    /// <summary>
    ///     Enumerates a source that may throw, ending the sequence on failure instead of propagating.
    ///     Lets one failing provider fall through to the next instead of killing the whole search.
    /// </summary>
    public static async IAsyncEnumerable<T> Guarded<T>(this IAsyncEnumerable<T> source, ILogger logger, string context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var enumerator = source.GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            try
            {
                if (!await enumerator.MoveNextAsync()) yield break;
            }
            catch (Exception e)
            {
                logger.Error(e, "Streaming from {Context} failed", context);
                yield break;
            }

            yield return enumerator.Current;
        }
    }
}