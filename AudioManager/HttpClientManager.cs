namespace Audio;

public static class HttpClientManager
{
    private static HttpClient HttpClient { get; } = new();

    public static void InitializeCookies()
    {
        // TODO add functionality
        throw new NotImplementedException();
    }

    public static HttpClient GetHttpClient()
    {
        return HttpClient;
    }
}