using System.Runtime.CompilerServices;
using Serilog;

namespace Gaida.Core.Utils;

public static class Streaming
{
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
