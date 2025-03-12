namespace Audio.Utils;

public static class Generation
{
    private static readonly Random Rng = new();

    public static string RandomString(int length, bool bad_symbols = false)
    {
        return new string(Enumerable
            .Repeat(
                $"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789{bad_symbols switch { true => "_-.", false => "" }}",
                length).Select(s => s[new Random(Rng.Next(int.MaxValue)).Next(s.Length)]).ToArray());
    }
}