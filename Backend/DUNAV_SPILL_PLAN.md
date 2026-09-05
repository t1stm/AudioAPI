# Plan: Dunav spills cached bodies to disk

> **Status:** implemented. All eight sections landed; the solution builds clean and
> `dotnet run --project Dunav -- --self-check` passes both checks (coalescing, and five readers
> following a growing body). Verified end to end against a fake upstream streaming 6 MiB over 3s:
> three clients racing a cold fetch plus one arriving after completion all returned byte-identical
> bodies off a single upstream fetch, a range request returned a correct 206, and a completed entry
> answers with `Accept-Ranges: bytes` and a real `Content-Length`.
>
> The headline measurement: **RSS went 342.75 MiB to 342.72 MiB while serving 240 MiB** to 40
> concurrent clients across 8 distinct keys. Resident memory no longer tracks what is cached.
>
> Also verified live: the boot wipe (3 planted stale files cleared at startup), and the §4 eviction
> race — unlinking a body behind a live cache entry produced exactly one 503 and a `Forget`, and the
> immediate retry re-fetched the full correct body.

Goal: Dunav's resident memory becomes a function of *concurrent connections*, not of catalog
size. Today a 45-minute retention window holds every fetched body as `byte[]` chunks on the GC
heap, and under load the pod OOMs well before the configured ceiling is enforced.

This is the disk-spill step already anticipated in `SERVICE_SPLIT_PLAN.md:345-349`, and the
`{codec}-{bitrate}-{sha256(id)}` key shape chosen there (`CacheService.cs:44-58`) is already a
valid filename, so no key redesign is needed.

**Net effect is a deletion.** `StreamSpreader` leaves Dunav entirely, taking the subscriber
plumbing, the range-request buffer copy, and the project reference to `Gaida.Core` with it.
`Gaida.Core.Streams` stays where it is — `Gaida.Pods.MusicDatabase/Program.cs:237` and the platform
getters still use it, and they are not in scope here.

## Why memory grows unbounded today

Four independent causes. Only the last is the one people expect.

1. **`StreamSpreader.GetBufferedBytesAsync` (`StreamSpreader.cs:78`) allocates roughly 3x the body
   per range request.** Every chunk is copied into a growing `MemoryStream` (which doubles its
   backing array as it fills), then `.ToArray()` copies the whole thing again, then
   `AudioController.BufferedRangeResponse` hands that array to `File(bytes, …)`, which holds it
   for the life of the response. Media players issue range requests constantly. No ceiling in the
   codebase accounts for this spike.

2. **`Dunav__MaxBytes` is set to exactly the pod limit** (`compose.yaml`, `4294967296`). The
   ceiling counts `Spreader.Length` — payload bytes only. Actual RSS is payload, plus
   `List<(byte[], int, int)>` overhead, plus Gen2/LOH fragmentation from the ~80KB chunks
   `CopyToAsync` produces, and Server GC does not return that to the OS. Measured RSS for this
   shape typically runs 1.5–2x payload.

3. **`CacheService.EvictOverCeiling` (`CacheService.cs:174`) can be structurally unable to
   evict.** `total` sums *all* live entries, but the eviction loop filters on
   `x.Entry.Spreader.Closed`. With enough concurrent cold starts, nothing is eligible and the
   cache grows past the ceiling with eviction running and succeeding at nothing. The sweep is
   also on a one-minute timer (`CacheService.cs:33`), so a minute of load overshoots freely.

4. **`StreamSpreader.Write` (`StreamSpreader.cs:47`) does `Data.Add(([.. buffer], offset, count))`**
   — that copies the entire array, not the `(offset, count)` slice. The async path
   (`StreamSpreader.cs:60`) is correct; the sync path is a landmine for any caller that passes a
   large buffer with a small count.

Causes 1 and 4 disappear with the code. Causes 2 and 3 stop mattering, because the ceiling
becomes a *disk* ceiling against 1.4T of free space rather than a RAM ceiling against 4GB.

## Why plain files and not tmpfs

The natural instinct is a tmpfs tier — fast, memory-backed, cleaned up on demand. It is the wrong
tool here, for a reason specific to running under a container memory limit:

**tmpfs pages are charged to the memory cgroup of the process that faults them in, and they are
not reclaimable.** The kernel can only swap them out, and the container has no swap. A 512MB
tmpfs inside a 4GB pod is 512MB *of that 4GB*, and a burst past the limit is an OOM kill rather
than a cache miss. Regular file pages on a real filesystem are reclaimable: under pressure the
kernel drops them and the next read comes off NVMe.

Concretely, in this deployment:

| Path | What it actually is | Charged to the pod's cgroup? |
|---|---|---|
| Host `/tmp` bind-mounted in | tmpfs, 16G, unreclaimable | **Yes** — reintroduces the OOM |
| `tmpfs: /tmp` in compose | same, container-local | **Yes** |
| Container `/tmp`, nothing mounted | overlay2 on `/dev/nvme0n1p2`, 1.4T free | No — reclaimable page cache |

The third row is the target, and it requires no compose change at all.

The related instinct — keeping in-flight blobs in memory until every subscriber has consumed
them — is also worth naming and rejecting. One slow client would pin its blobs in RAM
indefinitely, which is precisely the failure being fixed. And bytes written and read back within
seconds never touch the platter anyway: they are served out of dirty page cache. The kernel
already implements the hot-in-RAM/cold-on-disk tiering, with reclaim that degrades instead of
OOM-killing.

## Why blobs are not needed: one writer, N independent readers, measured

The instinct to chunk into fixed-size blobs comes from the belief that a file cannot be opened
for reading while it is still being written. That is a Windows constraint, and even there it is
conditional, not absolute:

- **On Linux there is no mandatory locking.** `FileShare` is advisory; `tail -f` is this exact
  pattern. The container runs Linux, so this is the operative case.
- **On Windows** `FileShare` is enforced, but the rule is that all openers must *agree*. A writer
  opened with `FileShare.ReadWrite | FileShare.Delete` and readers opened the same way coexist
  fine. What breaks is a single opener taking the default `FileShare.Read` (or `.None`).

This is why every `FileStream` in this plan — writer and readers alike — specifies
`FileShare.ReadWrite | FileShare.Delete`. Those flags are load-bearing, not decoration.

Verified rather than assumed, with a standalone probe (one writer appending a 3 MiB payload in
97 KB chunks with a flush per chunk; three readers each opening their own `FileStream` at
different points during the write):

```
  writer: unlinked at 2425000 bytes, still writing
  reader-early: read 3145728 bytes
  reader-mid: read 3145728 bytes
  reader-late: read 3145728 bytes
OK: 3 concurrent readers, byte-identical, file unlinked mid-flight
  open-after-unlink: FileNotFoundException <- the eviction race
```

Two results, and the second one changed this plan:

1. All three readers returned the payload byte-identically while the writer was still appending,
   and **kept reading correctly after the file was unlinked underneath them** at 77% written.
   That is §5's eviction story confirmed: an open handle is unaffected by `File.Delete`.
2. Opening the same path *after* the unlink throws `FileNotFoundException`. So the eviction race
   in §4 is genuinely reachable and needs a real branch, not an optimistic comment. See §4.

Chunking into blobs would trade this for a blob index, per-blob lifetime, and reassembly across
boundaries — solving a problem the flags already solve.

## 1. `CacheEntry` becomes a file handle — `CacheEntry.cs`

```csharp
public class CacheEntry
{
    public required string Path { get; init; }

    /// <summary>Bytes written so far. For the eviction budget only -- readers do not consult it.</summary>
    public long Length;

    /// <summary>Set once the upstream body is complete. A reader that sees this and then reads 0 bytes is done.</summary>
    public volatile bool Closed;

    private TaskCompletionSource Signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes on the next <see cref="Publish"/>. Capture it *before* testing Closed, or you race a bump away.</summary>
    public Task Changed => Volatile.Read(ref Signal).Task;

    public void Publish(long length, bool closed)
    {
        Volatile.Write(ref Length, length);
        Closed = closed;
        Interlocked.Exchange(ref Signal,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).SetResult();
    }

    public string ContentType { get; set; } = "application/octet-stream";
    public string? ContentDisposition { get; set; }
    public string? ETag { get; set; }
}
```

**`Length` is not part of the read protocol.** It exists solely so `EvictOverCeiling` can sum a
budget without stat-ing every file. Readers never consult it, because a byte stream cannot tear in
a way that matters: a reader that catches the writer mid-chunk simply gets fewer bytes and picks up
the rest on its next read. Content correctness is free; the only thing a follower actually needs to
know is whether `read()` returning 0 means *wait* or *done*, and that is exactly what `Closed`
answers.

That leaves **one** ordering rule, and it needs a comment in the code:

- **A reader captures `Changed` before testing `Closed`.** Otherwise the writer can publish in the
  gap between the test and the await, and the reader sleeps on a signal that has already fired.
  There is no symmetric rule on the writer side and no truncation hazard — see §3.

## 2. `FetchAsync` writes to the file — `CacheService.cs:110-160`

Replace `PumpAsync`'s `source.CopyToAsync(spreader)` with an explicit loop, because `Length` may
only be published for bytes that have actually reached the OS. `FileStream`'s internal buffer
would otherwise let a reader see a length it cannot read yet.

```csharp
private static async Task PumpAsync(HttpResponseMessage response, Stream source, CacheEntry entry)
{
    try
    {
        await using var file = new FileStream(entry.Path, FileMode.Create, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete, 0, FileOptions.Asynchronous);

        var buffer = new byte[64 * 1024];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read));
            await file.FlushAsync();          // to the OS, not fsync -- enough for a reader's handle to see it
            entry.Publish(written += read, false);
        }
    }
    finally
    {
        entry.Publish(Volatile.Read(ref entry.Length), true);
        response.Dispose();
    }
}
```

`FileShare.Delete` on both writer and reader is load-bearing: it is what lets eviction unlink the
file while requests still hold handles to it.

`GetOrStartAsync` (`CacheService.cs:70`) constructs the entry with a `Path` instead of a
`Spreader`; the failure branch deletes the file instead of disposing the spreader.

## 3. Streaming reads follow the growing file — `AudioController.cs:116-176`

The entire `ConcurrentQueue` / two-semaphore / `StreamSubscriber` block is replaced by:

```csharp
private async Task StreamToResponse(CacheEntry entry, CancellationToken ct)
{
    await using var file = new FileStream(entry.Path, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous);

    var buffer = new byte[64 * 1024];
    while (!ct.IsCancellationRequested)
    {
        var changed = entry.Changed;                 // capture BEFORE testing Closed -- see the ordering rule in section 1
        var closed = entry.Closed;

        var read = await file.ReadAsync(buffer, ct); // read() is the truth; no Length arithmetic
        if (read > 0) { await Response.Body.WriteAsync(buffer.AsMemory(0, read), ct); continue; }

        if (closed) break;                           // Closed observed, then a real read returned 0 -> genuinely drained
        await changed.WaitAsync(ct);
    }
}
```

The loop cannot truncate. It only exits when a real `read()` returned 0 bytes *and* `Closed` was
already observed before that read, so any byte the writer had flushed is necessarily consumed
first. That property does not depend on the writer's publish order, which is why §1 has one
ordering rule instead of two.

Memory per client is one 64KB buffer, flat, regardless of body size. A client that goes away
cancels its own read and takes nothing else down with it — which also retires the "one dead
subscriber must not poison the spreader" defence at `StreamSpreader.cs:112-118`.

## 4. Range requests stop copying the body — `AudioController.cs:102-114`

`BufferedRangeResponse` and `GetBufferedBytesAsync` both delete. When `entry.Closed`:

```csharp
var stream = new FileStream(entry.Path, FileMode.Open, FileAccess.Read,
    FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous);
return File(stream, entry.ContentType, enableRangeProcessing: true);
```

`FileStreamResult` handles the `Range` parsing, the 206, the `Content-Range` header and the 416
case. The stream is seekable, so it serves the requested window without materialising the rest.
This is the single largest reduction in peak memory in the plan.

Opening the stream ourselves rather than using `PhysicalFile` is deliberate: it lets us pass
`FileShare.Delete`, and it puts the open where we can catch it.

**Both read paths must handle the open losing a race with eviction.** The probe above proves this
is reachable, not theoretical. `TryGet`/`GetOrStartAsync` refresh expiry immediately before
`Respond` runs, so the expiry sweep cannot be the culprit — but `EvictOverCeiling` can fire in
that window. The entry is genuinely gone at that point, so the repair is to drop the stale key and
let the client retry into a fresh fetch:

```csharp
catch (FileNotFoundException)
{
    cache.Forget(key);            // the dictionary entry outlived its file
    return StatusCode(503);       // retriable; the next request re-fetches upstream
}
```

`CacheService.Forget(key)` is a two-line `TryRemove` from both `CachedEntries` and `ExpireTimes`.
Not a refcount, not a deferred-delete queue: the window is microseconds wide and a retry is
correct behaviour for a cache miss.

The `Response.Headers.AcceptRanges = entry.Closed ? "bytes" : "none"` guard at
`AudioController.cs:96` keeps its existing meaning and stays as-is.

## 5. Eviction unlinks — `CacheService.cs:196-206`

`Evict` calls `File.Delete(entry.Path)` in place of `Spreader.Dispose()`, inside a try/catch (a
missing file is already the desired end state). On Linux the unlink is immediate but the inode
survives until the last open handle closes, so in-flight responses finish off their own handle
and the space is reclaimed when they do — measured in the probe above, where three readers each
completed a byte-exact 3 MiB after the file was unlinked at 77%. No refcounting, no
deferred-delete queue; the only case needing code is a reader that has not opened yet, handled in
§4.

One consequence worth stating: unlinked-but-open files still occupy disk. A `df` reading can
therefore lag `Dunav__MaxBytes` by the size of whatever is currently streaming. Against 1.4T free
this is noise, but it is why the budget should not be tuned to the last gigabyte.

`EvictOverCeiling` keeps its shape; `x.Entry.Spreader.Length` becomes `x.Entry.Length`. The
`Closed`-only eligibility filter (cause 3 above) can stay: it is now a disk-space question
against 1.4T rather than a RAM question against 4GB, so being briefly unable to evict is no
longer a crash.

Add a boot wipe in the `CacheService` constructor — `Directory.CreateDirectory(dir)` then delete
its contents. The dictionary is empty at boot so nothing can reference a leftover file, and this
is what makes "no `.part` file, no atomic rename, no startup reconciliation" safe.

## 6. Configuration — `compose.yaml`

```yaml
  dunav:
    environment:
      Dunav__CacheDir: /tmp/dunav          # container-local overlay2, NOT a tmpfs mount
      Dunav__MaxBytes: "21474836480"       # 20 GiB of disk, against 1.4T free on /
    mem_limit: 1g
```

Three things to get right here:

- **Do not add a `tmpfs:` entry or a `/tmp` bind mount for this service.** That is the whole point
  of §"Why plain files and not tmpfs"; a mount silently converts the fix back into the bug.
- `Dunav__MaxBytes` changes units in spirit — it is now a disk budget. Raising it from 4 GiB is
  the point of the exercise.
- `mem_limit` can drop from 6g. Steady-state RAM becomes the ASP.NET runtime plus roughly 128KB
  per in-flight connection. 1g is generous; watch it before trimming further.

The `/tmp/dunav` files are also, incidentally, immutable finished encodes on a normal filesystem,
which is what would later let a static file server answer hits without going through Dunav at
all. Not in scope.

## 7. Drop the `Gaida.Core` reference — `Dunav.csproj`, `Dunav/Dockerfile`

Dunav references `Gaida.Core` for `StreamSpreader` / `StreamSubscriber` / `StreamStatus` and
nothing else — the comment at `Dunav.csproj:17` says so, and a grep across `Dunav/` confirms it.
Once §1–§5 land, delete:

- the `<ProjectReference Include="..\Gaida\Gaida.Core\Gaida.Core.csproj"/>` block
- both `COPY … Gaida/Gaida.Core/…` lines from `Dunav/Dockerfile`

Dunav's image stops rebuilding when unrelated platform code changes.

## 8. Self-checks — `SelfCheck.cs`

`CoalescingAsync` asserts on `results.Select(r => r!.Spreader)` at `SelfCheck.cs:27`; that becomes
`.Path`. It otherwise stands unchanged and must still report exactly one upstream fetch for 20
racers.

Add one more, because the follower read in §3 is the only genuinely non-trivial logic being
introduced and the ordering rule in §1 is exactly the kind of thing that works in testing and
fails under load:

**`FollowAsync`** — write a known multi-megabyte payload into an entry in small chunks with a
delay between them, start readers at several different points during the write, and assert every
reader's output is byte-identical to the payload and that each terminates. Include one reader that
starts after `Closed`, and unlink the file mid-write to pin §5's behaviour. That single check
covers the publish/observe ordering, the wake-up-on-bump path, the read-after-close path, and
read-after-unlink.

The standalone probe from §"Why blobs are not needed" is already this check in miniature and ports
across nearly unchanged — it is where the output quoted there came from.

Both run under the existing `dotnet run --project Dunav -- --self-check`.

## Order of work

§1 and §2 together (nothing compiles between them), then §3, then §4, then §5. §6 is a deploy-time
change and §7 is cleanup once nothing references the old types. §8 lands with §3.

## Deliberately skipped

- **Persistence across restarts.** The files would survive, but reconstructing headers and
  validating partial writes is a real feature. Boot wipe instead. Add it if a Dunav restart during
  peak measurably hurts.
- **1 MB blob chunking with an index.** The kernel's page granularity already is the chunking, and
  an explicit blob index is a second cache to keep coherent with the first.
- **Explicit memory/disk tiering.** Page cache is the tier, and it has better eviction than
  anything worth hand-writing here.
- **`fsync` on the written files.** These are cache entries; losing them to a power cut is a cache
  miss, not data loss.
- **Serving `/tmp/dunav` directly from the reverse proxy.** Interesting, and much easier once this
  lands, but it is a separate change to a separate component.
