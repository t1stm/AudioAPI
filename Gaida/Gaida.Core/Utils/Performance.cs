namespace Gaida.Core.Utils;

public static class Performance
{
    /// <summary>
    ///     Creates a slice of a string that lasts until the search needle is found.
    /// </summary>
    /// <param name="haystack">The source string.</param>
    /// <param name="needle">The separator.</param>
    /// <returns>A slice if the needle is found or the haystack if not.</returns>
    public static ReadOnlySpan<char> SliceTo(this ReadOnlySpan<char> haystack, ReadOnlySpan<char> needle)
    {
        var indexOf = haystack.IndexOf(needle, StringComparison.Ordinal);
        return indexOf < 0 ? haystack : haystack[..indexOf];
    }

    /// <summary>
    ///     Creates a slice of a string that starts after the last occurrence of the search needle.
    /// </summary>
    /// <param name="haystack">The source string.</param>
    /// <param name="needle">The separator.</param>
    /// <returns>A slice if the needle is found or the haystack if not.</returns>
    public static ReadOnlySpan<char> SliceAfter(this ReadOnlySpan<char> haystack, ReadOnlySpan<char> needle)
    {
        var indexOf = haystack.LastIndexOf(needle, StringComparison.Ordinal);
        return indexOf < 0 ? haystack : haystack[(indexOf + needle.Length)..];
    }
}
