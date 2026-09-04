namespace Gaida.Core.Utils;

public static class Generation
{
    private const string NormalChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const string BadChars = NormalChars + "_-.";

    public static string RandomString(int length, bool badSymbols = false)
    {
        return new string(Random.Shared.GetItems<char>(badSymbols ? BadChars : NormalChars, length));
    }
}