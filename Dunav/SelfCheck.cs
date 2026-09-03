using System.Net;
using System.Net.Http.Headers;
using Serilog;

namespace Dunav;

/// <summary>
///     Smallest runnable proof that <see cref="CacheService.GetOrStartAsync" /> coalesces: N concurrent
///     racers for the same key must trigger exactly one upstream HTTP call. Run with
///     <c>dotnet run --project Dunav -- --self-check</c>.
/// </summary>
internal static class SelfCheck
{
    public static async Task<bool> CoalescingAsync()
    {
        var fetchCount = 0;
        var handler = new CountingHandler(() => Interlocked.Increment(ref fetchCount));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://unit-test.local") };
        var cache = new CacheService(http, Log.Logger, new ConfigurationBuilder().Build());

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => cache.GetOrStartAsync("check-key",
            entry => cache.FetchAsync(entry, "/probe", CancellationToken.None), out var started)));

        var ok = fetchCount == 1
                 && results.All(r => r is not null)
                 && results.Select(r => r!.Spreader).Distinct().Count() == 1;

        Console.WriteLine(ok
            ? $"OK: {results.Length} concurrent requests -> {fetchCount} upstream fetch(es)"
            : $"FAIL: {results.Length} concurrent requests -> {fetchCount} upstream fetch(es), " +
              $"{results.Count(r => r is null)} null result(s)");

        return ok;
    }

    private sealed class CountingHandler(Action onSend) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            onSend();

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
            return Task.FromResult(response);
        }
    }
}