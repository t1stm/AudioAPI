namespace Gaida.Core.Utils;

public static class Performance
{
    /// <param name="haystack">The source string.</param>
    extension(ReadOnlySpan<char> haystack)
    {
        /// <summary>
        ///     Creates a slice of a string that lasts until the search needle is found.
        /// </summary>
        /// <param name="needle">The separator.</param>
        /// <returns>A slice if the needle is found or the haystack if not.</returns>
        public ReadOnlySpan<char> SliceTo(ReadOnlySpan<char> needle)
        {
            var indexOf = haystack.IndexOf(needle, StringComparison.Ordinal);
            return indexOf < 0 ? haystack : haystack[..indexOf];
        }

        /// <summary>
        ///     Creates a slice of a string that starts after the last occurrence of the search needle.
        /// </summary>
        /// <param name="needle">The separator.</param>
        /// <returns>A slice if the needle is found or the haystack if not.</returns>
        public ReadOnlySpan<char> SliceAfter(ReadOnlySpan<char> needle)
        {
            var indexOf = haystack.LastIndexOf(needle, StringComparison.Ordinal);
            return indexOf < 0 ? haystack : haystack[(indexOf + needle.Length)..];
        }
    }
}