namespace AudioManagement.Utils;

public static class Performance
{
    /// <summary>
    ///     Creates a slice of a string that lasts until the search needle is found.
    /// </summary>
    /// <param name="haystack">The source string.</param>
    /// <param name="needle">The separator.</param>
    /// <returns>A slice if the needle is found or the haystack if not.</returns>
    public static string SliceTo(this string haystack, string needle)
    {
        var indexOf = haystack.IndexOf(needle, StringComparison.Ordinal);
        return indexOf < 0 ? haystack : haystack[..indexOf];
    }

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
    ///     Creates a slice of a string that starts when the last occurance of the search needle is found.
    /// </summary>
    /// <param name="haystack">The source string.</param>
    /// <param name="needle">The separator.</param>
    /// <returns>A slice if the needle is found or the haystack if not.</returns>
    public static string SliceAfter(this string haystack, string needle)
    {
        var indexOf = haystack.LastIndexOf(needle, StringComparison.Ordinal);
        return indexOf < 0 ? haystack :
            indexOf + needle.Length > haystack.Length ? haystack :
            haystack[..indexOf];
    }

    /// <summary>
    ///     Creates a slice of a string that starts when the last occurance of the search needle is found.
    /// </summary>
    /// <param name="haystack">The source string.</param>
    /// <param name="needle">The separator.</param>
    /// <returns>A slice if the needle is found or the haystack if not.</returns>
    public static ReadOnlySpan<char> SliceAfter(this ReadOnlySpan<char> haystack, ReadOnlySpan<char> needle)
    {
        var indexOf = haystack.LastIndexOf(needle, StringComparison.Ordinal);
        return indexOf < 0 ? haystack :
            indexOf + needle.Length > haystack.Length ? haystack :
            haystack[..indexOf];
    }
}