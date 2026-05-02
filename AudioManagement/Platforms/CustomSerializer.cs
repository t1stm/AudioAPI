using System.Text.Encodings.Web;
using System.Text.Json;

namespace AudioManagement.Platforms;

public static class CustomSerializer
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ToJson(this IEnumerable<PlatformResult> results)
    {
        return '[' + string.Join(',', results.Select(r => r.SerializeSelf())) + ']';
    }
}