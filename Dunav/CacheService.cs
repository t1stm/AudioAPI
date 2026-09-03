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
///     <c>Func&lt;FFmpegEncoder,...&gt;</c> to fetch-upstream-into-a-file. Also owns expiry, the byte-ceiling
///     LRU eviction the old code never had, and the on-disk bodies themselves.
/// </summary>
public class CacheService
{
    private readonly ConcurrentDictionary<string, Lazy<Task<CacheEntry?>>> CachedEntries = new();
    private readonly ConcurrentDictionary<string, DateTime> ExpireTimes = new();
    private readonly string CacheDir;
    private readonly HttpClient Http;
    private readonly long MaxBytes;
    private readonly TimeSpan Retention;
    private readonly Timer SweepTimer;

    public CacheService(HttpClient http, ILogger logger, IConfiguration configuration)
    {
        Http = http;
        Logger = logger;
        Retention = TimeSpan.FromMinutes(configuration.GetValue("Dunav:RetentionMinutes", 45));

        // A disk budget, not a memory one. Bodies live in CacheDir; what this bounds is how much of the
        // filesystem they may occupy, so it is sized against free disk rather than the pod's mem_limit.
        MaxBytes = configuration.GetValue("Dunav:MaxBytes", 20L * 1024 * 1024 * 1024);

        // Deliberately NOT a tmpfs mount: tmpfs pages are charged to the container's memory cgroup and
        // cannot be reclaimed, which is the OOM this whole design exists to avoid. Ordinary files on an
        // ordinary filesystem give reclaimable page cache instead. See DUNAV_SPILL_PLAN.md.
        CacheDir = configuration.GetValue("Dunav:CacheDir", "/tmp/dunav");
        Directory.CreateDirectory(CacheDir);

        // Wipe on boot. CachedEntries starts empty, so nothing can reference a leftover file, and this is
        // what lets the writer use the final filename directly -- no .part suffix, no atomic rename, no
        // startup reconciliation to decide whether a stray file is complete.
        foreach (var stale in Directory.EnumerateFiles(CacheDir))
            try
            {
                File.Delete(stale);
            }
            catch (IOException exception)
            {
                Logger.Warning(exception, "Could not remove stale cache file {File}", stale);
            }

        SweepTimer = new Timer(TimeSpan.FromMinutes(1)) { Enabled = true };
        SweepTimer.Elapsed += Sweep;
    }

    private ILogger Logger { get; }

    /// <summary>
    ///     Hex so the key is valid as a filename -- which is exactly what it now is: every key names a file
    ///     under <c>Dunav:CacheDir</c>.
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
            var entry = new CacheEntry
            {
                // Named for its key and kept until evicted, rather than a self-deleting scratch file.
                Body = new StreamSpreader(Path.Combine(CacheDir, key), false)
            };
            if (await start(entry)) return entry;

            // A failed start must not stay cached, or every later request inherits the failure.
            Forget(key);
            Delete(entry);
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

    /// <summary>
    ///     Drops <paramref name="key" /> from the index without touching its file. Used when a reader finds
    ///     the file already unlinked -- the entry has outlived its body and must not be handed out again.
    /// </summary>
    public void Forget(string key)
    {
        CachedEntries.TryRemove(key, out _);
        ExpireTimes.TryRemove(key, out _);
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
        _ = PumpAsync(response, upstreamStream, entry, upstreamPath);
        return true;
    }

    /// <summary>
    ///     Copies the upstream body into the entry's spreader, which flushes and publishes as it goes so
    ///     followers see bytes as they land.
    /// </summary>
    private async Task PumpAsync(HttpResponseMessage response, Stream source, CacheEntry entry, string upstreamPath)
    {
        var failed = false;
        try
        {
            await source.CopyToAsync(entry.Body);
        }
        catch (Exception exception)
        {
            // A body that died halfway is on disk and looks complete once closed. Serving it would hand
            // clients truncated audio with a confident Content-Length, so drop the key: the next request
            // re-fetches instead of inheriting the stump. Readers already attached still drain what
            // arrived -- their handle outlives the unlink.
            failed = true;
            Logger.Warning(exception, "Upstream body failed mid-transfer for {Path}", upstreamPath);
        }
        finally
        {
            await entry.Body.CloseAsync();
            response.Dispose();

            if (failed)
            {
                Forget(KeyOf(entry));
                Delete(entry);
            }
        }
    }

    private string KeyOf(CacheEntry entry)
    {
        return Path.GetFileName(entry.Body.Path);
    }

    private void Delete(CacheEntry entry)
    {
        try
        {
            entry.Body.Dispose();
            File.Delete(entry.Body.Path);
        }
        catch (IOException exception)
        {
            Logger.Warning(exception, "Could not delete cache file {File}", entry.Body.Path);
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
    ///     LRU eviction once total cached bytes cross <c>Dunav:MaxBytes</c>.
    /// </summary>
    /// <remarks>
    ///     Only finished entries are eligible, so a burst of concurrent cold starts can briefly hold the
    ///     total above the ceiling. That was a crash when the bodies were on the heap; against a disk budget
    ///     it is just a temporary overshoot. Note also that an unlinked file still occupies space until the
    ///     last reader closes its handle, so <c>df</c> can lag this figure by whatever is currently
    ///     streaming -- do not tune the budget to the last gigabyte.
    /// </remarks>
    private void EvictOverCeiling()
    {
        if (MaxBytes <= 0) return;

        var live = CachedEntries
            .Where(kv => kv.Value is { IsValueCreated: true, Value.IsCompletedSuccessfully: true } &&
                         kv.Value.Value.Result is not null)
            .Select(kv => (kv.Key, Entry: kv.Value.Value.Result!,
                Expire: ExpireTimes.GetValueOrDefault(kv.Key, DateTime.MinValue)))
            .ToList();

        var total = live.Sum(x => x.Entry.Body.Length);
        if (total <= MaxBytes) return;

        // ExpireTimes doubles as a recency signal: every TryGet/GetOrStart refreshes it to now+Retention,
        // so the smallest expiry is also the least recently used. Only finished entries are eligible, so
        // eviction never unlinks the file out from under a fetch still writing to it.
        foreach (var (key, entry, _) in live.Where(x => x.Entry.Body.Closed).OrderBy(x => x.Expire))
        {
            if (total <= MaxBytes) break;
            if (!Evict(key, "over byte ceiling")) continue;
            total -= entry.Body.Length;
        }
    }

    private bool Evict(string key, string reason)
    {
        ExpireTimes.TryRemove(key, out _);
        if (!CachedEntries.TryRemove(key, out var lazy)) return false;

        Logger.Information("Evicting cache entry {Key} ({Reason})", key, reason);

        // Unlink, do not wait. On Linux the directory entry goes immediately but the inode survives until
        // the last open handle closes, so responses already streaming finish off their own handle and the
        // space comes back when they do. A reader that has not opened yet gets FileNotFoundException, which
        // AudioController turns into a retriable 503.
        if (lazy is { IsValueCreated: true, Value.IsCompletedSuccessfully: true } && lazy.Value.Result is { } entry)
            Delete(entry);
        return true;
    }
}