namespace AudioManagement.Utils;

public static class Generation
{
    private static readonly Random Rng = new();

    public static string RandomString(int length, bool badSymbols = false)
    {
        const string normalChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        const string badChars = normalChars + "_-.";

        var rng = new Random(Rng.Next(int.MaxValue));

        return string.Concat(Enumerable
            .Repeat(
                badSymbols ? badChars : normalChars, length)
            .Select(s => s[rng.Next(s.Length)]));
    }
}