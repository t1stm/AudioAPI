namespace Audio.Utils;

public static class Generation
{
    private static readonly Random Rng = new();

    public static string RandomString(int length, bool bad_symbols = false)
    {
        const string NORMAL_CHARS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        const string BAD_CHARS = NORMAL_CHARS + "_-.";

        var rng = new Random(Rng.Next(int.MaxValue));
        
        return string.Concat(Enumerable
            .Repeat(
                bad_symbols ? BAD_CHARS : NORMAL_CHARS, length)
            .Select(s => s[rng.Next(s.Length)]));
    }
}