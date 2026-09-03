using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Timers;
using Gaida.Core.Streams;
using ILogger = Serilog.ILogger;
using Timer = System.Timers.Timer;

namespace Dunav;

/// <summary>
///     One upstream fetch per cache key, however many clients race for it -- coalescing lifted from
///     <c>Gaida.API/ManagerService.cs</c>'s <c>GetOrStartEncoderAsync</c>, adapted from
///     <c>Func&lt;FFmpegEncoder,...&gt;</c> to fetch-upstream-into-<see cref="StreamSpreader" />. Also owns
///     expiry and the byte-ceiling LRU eviction the old code never had.
/// </summary>
public class CacheService
{
    private readonly ConcurrentDictionary<string, Lazy<Task<CacheEntry?>>> CachedEntries = new();
    private readonly ConcurrentDictionary<string, DateTime> ExpireTimes = new();
    private readonly HttpClient Http;
    private readonly long MaxBytes;
    private readonly TimeSpan Retention;
    private readonly Timer SweepTimer;

    public CacheService(HttpClient http, ILogger logger, IConfiguration configuration)
    {
        Http = http;
        Logger = logger;
        Retention = TimeSpan.FromMinutes(configuration.GetValue("Dunav:RetentionMinutes", 45));
        MaxBytes = configuration.GetValue("Dunav:MaxBytes", 4L * 1024 * 1024 * 1024);

        SweepTimer = new Timer(TimeSpan.FromMinutes(1)) { Enabled = true };
        SweepTimer.Elapsed += Sweep;
    }

    private ILogger Logger { get; }

    /// <summary>
    ///     Hex so the key is valid as a filename -- deliberate, so a later disk-spill cache is a fallback
    ///     branch rather than a redesign (see SERVICE_SPLIT_PLAN.md "When load does increase").
    /// </summary>
    // ponytail: only SHA-256 hex used here, no truncation/base64 tradeoffs considered -- id strings are
    // short (a video ID or a local path), so collision risk and key length are both non-issues.
    public static string HashId(string id)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    }

    public static string RawKey(string id)
    {
        return $"raw-{HashId(id)}";
    }

    public static string EncodedKey(string codec, int bitrate, string id)
    {
        return $"{codec}-{bitrate}-{HashId(id)}";
    }

    /// <summary>
    ///     Starts the fetch for <paramref name="key" /> at most once, however many requests race for it: the
    ///     losers await the winner's task instead of issuing a second upstream request for the same bytes.
    /// </summary>
    /// <param name="start">Feeds the entry's spreader from upstream. Returns <c>false</c> when the fetch could not be started.</param>
    /// <param name="started">
    ///     <c>true</c> for the single caller whose call actually started the fetch; every racer that found the
    ///     key already there gets <c>false</c>. Preload answers 202 or 200 off this.
    /// </param>
    public Task<CacheEntry?> GetOrStartAsync(string key, Func<CacheEntry, Task<bool>> start, out bool started)
    {
        // Built before the add so that the add is the only race: GetOrAdd's factory overload may run for more
        // than one caller, and then two of them would each believe they started the fetch. A Lazy that loses
        // is discarded before it ever calls upstream.
        var lazy = new Lazy<Task<CacheEntry?>>(async () =>
        {
            var entry = new CacheEntry { Spreader = new StreamSpreader() };
            if (await start(entry)) return entry;

            // A failed start must not stay cached, or every later request inherits the failure.
            CachedEntries.TryRemove(key, out _);
            ExpireTimes.TryRemove(key, out _);
            entry.Spreader.Dispose();
            return null;
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        var cached = CachedEntries.GetOrAdd(key, lazy);
        started = ReferenceEquals(cached, lazy);

        ExpireIn(key);
        return cached.Value;
    }

    /// <summary>The fetch already running or finished for <paramref name="key" />, refreshing its expiry.</summary>
    public bool TryGet(string key, [NotNullWhen(true)] out Task<CacheEntry?>? entry)
    {
        if (!CachedEntries.TryGetValue(key, out var lazy))
        {
            entry = null;
            return false;
        }

        ExpireIn(key);
        entry = lazy.Value;
        return true;
    }

    private void ExpireIn(string key)
    {
        ExpireTimes[key] = DateTime.UtcNow.Add(Retention);
    }

    /// <summary>
    ///     Fetches <paramref name="upstreamPath" /> (relative to <c>Gaida:Url</c>) into <paramref name="entry" />'s
    ///     spreader. Returns once headers are read and the body copy is subscribed -- not once the body is
    ///     finished -- so callers get progressive streaming the same way <c>FFmpegEncoder.Convert</c> does.
    /// </summary>
    public async Task<bool> FetchAsync(CacheEntry entry, string upstreamPath, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.GetAsync(upstreamPath, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Upstream fetch failed for {Path}", upstreamPath);
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            Logger.Warning("Upstream returned {Status} for {Path}", response.StatusCode, upstreamPath);
            response.Dispose();
            return false;
        }

        entry.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        entry.ContentDisposition = response.Content.Headers.ContentDisposition?.ToString();
        entry.ETag = response.Headers.ETag?.Tag;

        var upstreamStream = await response.Content.ReadAsStreamAsync(cancellationToken);

        // Fire-and-forget on purpose, and deliberately not tied to the triggering request's cancellation
        // token: the fetch outlives the request that started it, same as GetContentDataAsync being called
        // with CancellationToken.None in Gaida.API's StartEncode.
        _ = PumpAsync(response, upstreamStream, entry.Spreader);
        return true;
    }

    private static async Task PumpAsync(HttpResponseMessage response, Stream source, StreamSpreader spreader)
    {
        try
        {
            await source.CopyToAsync(spreader);
        }
        finally
        {
            await spreader.CloseAsync();
            response.Dispose();
        }
    }

    private void Sweep(object? sender, ElapsedEventArgs elapsedEventArgs)
    {
        var now = DateTime.UtcNow;

        foreach (var (key, expire) in ExpireTimes)
        {
            if (expire > now) continue;
            Evict(key, "expired");
        }

        EvictOverCeiling();
    }

    /// <summary>
    ///     LRU eviction once total cached bytes cross <c>Dunav:MaxBytes</c> -- today's expiry loop has
    ///     no size bound at all, which is the first thing that breaks under load (see SERVICE_SPLIT_PLAN.md).
    /// </summary>
    private void EvictOverCeiling()
    {
        if (MaxBytes <= 0) return;

        var live = CachedEntries
            .Where(kv => kv.Value is { IsValueCreated: true, Value.IsCompletedSuccessfully: true } &&
                         kv.Value.Value.Result is not null)
            .Select(kv => (kv.Key, Entry: kv.Value.Value.Result!,
                Expire: ExpireTimes.GetValueOrDefault(kv.Key, DateTime.MinValue)))
            .ToList();

        var total = live.Sum(x => x.Entry.Spreader.Length);
        if (total <= MaxBytes) return;

        // ExpireTimes doubles as a recency signal: every TryGet/GetOrStart refreshes it to now+Retention,
        // so the smallest expiry is also the least recently used. Only finished entries (Spreader.Closed)
        // are eligible, so eviction never yanks the buffer out from under a fetch still in flight.
        foreach (var (key, entry, _) in live.Where(x => x.Entry.Spreader.Closed).OrderBy(x => x.Expire))
        {
            if (total <= MaxBytes) break;
            if (!Evict(key, "over byte ceiling")) continue;
            total -= entry.Spreader.Length;
        }
    }

    private bool Evict(string key, string reason)
    {
        ExpireTimes.TryRemove(key, out _);
        if (!CachedEntries.TryRemove(key, out var lazy)) return false;

        Logger.Information("Evicting cache entry {Key} ({Reason})", key, reason);
        if (lazy is { IsValueCreated: true, Value.IsCompletedSuccessfully: true })
            lazy.Value.Result?.Spreader.Dispose();
        return true;
    }
}