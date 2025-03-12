namespace Audio.Utils;

public static class Performance
{
    /// <summary>
    /// Creates a slice of a string that lasts until the search needle is found.
    /// </summary>
    /// <param name="haystack">The source string.</param>
    /// <param name="needle">The separator.</param>
    /// <returns>A slice if the needle is found or the haystack if not.</returns>
    public static string SliceTo(this string haystack, string needle)
    {
        var index_of = haystack.IndexOf(needle, StringComparison.Ordinal);
        return index_of < 0 ?
            haystack :
            haystack[..index_of];
    }
    
    /// <summary>
    /// Creates a slice of a string that lasts until the search needle is found.
    /// </summary>
    /// <param name="haystack">The source string.</param>
    /// <param name="needle">The separator.</param>
    /// <returns>A slice if the needle is found or the haystack if not.</returns>
    public static ReadOnlySpan<char> SliceTo(this ReadOnlySpan<char> haystack, ReadOnlySpan<char> needle)
    {
        var index_of = haystack.IndexOf(needle, StringComparison.Ordinal);
        return index_of < 0 ?
            haystack :
            haystack[..index_of];
    }

    /// <summary>
    /// Creates a slice of a string that starts when the last occurance of the search needle is found.
    /// </summary>
    /// <param name="haystack">The source string.</param>
    /// <param name="needle">The separator.</param>
    /// <returns>A slice if the needle is found or the haystack if not.</returns>
    public static string SliceAfter(this string haystack, string needle)
    {
        var index_of = haystack.LastIndexOf(needle, StringComparison.Ordinal);
        return index_of < 0 ?
            haystack :
            index_of + needle.Length > haystack.Length ?
                haystack :
                haystack[..index_of];
    }
    
    /// <summary>
    /// Creates a slice of a string that starts when the last occurance of the search needle is found.
    /// </summary>
    /// <param name="haystack">The source string.</param>
    /// <param name="needle">The separator.</param>
    /// <returns>A slice if the needle is found or the haystack if not.</returns>
    public static ReadOnlySpan<char> SliceAfter(this ReadOnlySpan<char> haystack, ReadOnlySpan<char> needle)
    {
        var index_of = haystack.LastIndexOf(needle, StringComparison.Ordinal);
        return index_of < 0 ?
            haystack :
            index_of + needle.Length > haystack.Length ?
                haystack :
                haystack[..index_of];
    }
}