namespace AudioManager.Platforms;

public static class CustomSerializer
{
    public static string ToJSON(this IEnumerable<PlatformResult> results)
    {
        return '[' + string.Join(',', results.Select(r => r.SerializeSelf())) + ']';
    }
}