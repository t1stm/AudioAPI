# Service split: Gaida as a proxy, platforms as pods

> **Status:** implemented and verified end to end under `compose.yaml`. All five services build
> clean, carry a runnable self-check (`dotnet run --project <name> -- --self-check`), and the full
> chain has been exercised live: search, raw download, encode, preload, YouTube resolve, room
> creation. Host ports are the 534x block. The reverse proxy is still the admin's to place in
> front — see phase 7.
>
> Measured on the running stack: cold encode 1.9 s for a 3:22 track, cache hit 0.004 s, and eight
> concurrent cold requests for one key produced exactly **one** ffmpeg process — the coalescing in
> phase 6 works against real traffic, not just its unit check.
>
> Since implementation: `SearchResultDto` gained `originalTitle` / `originalArtist`, and the
> untransliterated form is now the **displayed default** for `name` / `artist`, falling back to the
> romanized value. The romanized string is no longer on the wire; it stays server-side for search
> and matching. API.md documents the new shape.

Split the monolith into pods. Gaida.API becomes a proxy over platform services plus the encoder;
each platform owns its own storage and caching; Dunav owns the response fan-out; Selo owns rooms.

## Target topology

```
                  /Audio/Download*   Dunav :5341 ─┐    ┌─ Gaida.Pods.YouTube (Info.json, webm, yt-dlp)
                  /Audio/Preload*    (spreader)   │    │
client ─→ proxy ──┤                               ├──→ Gaida.API :5340 ─┼─ Gaida.Pods.MusicDatabase (/nvme0 library)
       (admin's)  ├ /Multiplayer* ─── Selo :5342 ─┘         (ffmpeg)    └─ Gaida.Pods.…
                  └ everything else ──────────────┘
```

Dunav: one fetch per `(codec,bitrate,id)`, N subscribers, expiry.

The three public services bind to loopback; platform pods publish nothing. The proxy is the admin's
(`nginx.example.conf` is a worked example), and its routing is load-bearing, not cosmetic: see
[§Coalescing](#coalescing-is-dunavs-job-now).

## What each service owns

| Service | Owns | State |
|---|---|---|
| **Gaida.Pods.\*** | search, resolve, classify, raw content, **its own caching** | node-pinned disk |
| **Gaida.API** | public contract, platform fan-out, DTO mapping, ffmpeg | none |
| **Dunav** | one fetch per `(codec,bitrate,id)`, fan-out, expiry, range requests | in-memory |
| **Selo** | rooms, queue, clock, chat | in-memory, sticky WS |

## The headline: `StreamSpreader` leaves Gaida.API entirely

`StreamSpreader`/`StreamSubscriber` is a push-based fan-out that exists because everything shares
objects in one process. Once every hop is an HTTP stream, `System.IO.Stream` is the abstraction and
the hand-rolled machinery goes:

| File | Now | After |
|---|---|---|
| `FFmpegEncoder.cs:19-94` (`Convert` + queue + semaphore + peek/write/dequeue), `:96-105` | 106 | ~15 |
| `Content.cs:300-360` (`StreamToResponse`) | 60 | 1 (`CopyToAsync`) |
| `AudioManager.cs` | 221 | ~60 |
| `ManagerService.cs` | 110 | ~15 |
| `QueryParser.cs` | 122 | 0 |

`StreamSpreader` survives in the two places it earns its keep: inside each platform pod (fan-out to
its own disk cache *and* the response) and inside Dunav (fan-out to N clients).

**Do not split the `Gaida.Core` assembly.** Just stop referencing it from Gaida.API. Deleting usage
is free; moving `FFmpegEncoder` and `Utils` into a new project buys nothing at runtime.

## Memory: this is strictly better than today

Today there are two caches, both retained 45 minutes:

| Cache | Holds |
|---|---|
| `AudioManager.CachedResults` (`AudioManager.cs:14`) | **source** bytes — YouTube webm, or a raw local FLAC |
| `ManagerService.CachedEncoders` (`ManagerService.cs:15`) | **encoded** output |

A local FLAC is ~30 MB of source retained alongside ~5 MB of opus. After the split Dunav holds only
the encoded side; source bytes live for the duration of one encode and go. The source cache was
buying almost nothing anyway — repeat YouTube fetches are already covered by the webm disk cache
(`Getter_LocalCache.cs:22`, Priority 99 at `:10`) and local re-reads are an NVMe read.

## Backpressure: why Dunav must buffer

With `CopyToAsync` chains a slow client propagates backpressure all the way up to the platform pod.
Today `StreamSpreader`'s unbounded buffer absorbs that. Dunav therefore cannot be a passthrough
proxy — its spreader has to be the buffer, or one slow listener stalls the encode every other
subscriber is reading. This is also why nginx/Varnish is not a substitute for Dunav.

## Platform pod contract

```
GET  /classify?query=          → {kind, id, error} | 404 not mine
GET  /resolve?id=              → ResultDto | 404
GET  /search?q=                → ResultDto[]    (404 = unsupported)
GET  /playlist?url=            → ResultDto[]    (404 = unsupported)
GET  /random?count=            → ResultDto[]    (404 = unsupported)
GET  /content?id=              → raw bytes + Content-Type + Content-Disposition
```

**404 means unsupported — no `/capabilities` handshake.** A capabilities fetch at boot is a startup
ordering dependency: Gaida crashloops if a platform pod is not up yet. 404 costs one wasted
round-trip per unsupported route across two or three pods, which is nothing.

`ResultDto` carries `id / name / artist / album / duration / thumbnailUrl` and **no `contentUrl`** —
platforms do not know the public host. Gaida adds it (`DiscoveryContracts.cs:66`).

Library-only routes (`/browse`, `/artist`, `/variant`) are just routes that one pod answers.
`Artist.cs:14,25` already names the platform in the public path, so that one is a direct map.

## Phases

### 1. Extract YouTube — `Gaida.Pods.YouTube`

First because it has the most self-contained state and is the one whose failure you most want
isolated. Everything it needs is already inside its own project:

- `YouTubeCacher` — the `Info.json` search cache (`YOUTUBE_CACHE_DB`)
- `YouTubeCacheProvider` + `GetterLocalCache` — the webm disk cache (`YOUTUBE_CACHE`)
- `GetterYouTubeExplode`, `GetterYtDlp`

`ISupportsCaching` (`AudioManager.cs:147`) becomes an internal concern of this pod. That resolves
the one hazard of the Dunav-only design: Dunav sits downstream of ffmpeg and would never see source
bytes, so the webm cache could not have lived there.

Move the hook into the loop that already has `this` and the spreader:

```csharp
// PlatformResult.cs:34 — GetContentDataAsync, inside the pod
foreach (var downloader in Downloaders)
{
    var result = await downloader.GetContentDataAsync(this, token);
    if (result is null) continue;
    if (this is ISupportsCaching caching) await caching.RunCacheProcess(result);
    return result;
}
```

### 2. `HttpPlatform` adapter — `Gaida.Core`

~40 lines so `AudioManager` does not change while platforms migrate one at a time:

```csharp
public class HttpPlatform(ILogger l, HttpClient http) : Platform(l), ISupportsSearch, ISupportsPlaylist, ISupportsRandomResults
{
    // 404 → empty. Implements every ISupports*; unsupported routes answer 404 and yield nothing.
}

public class HttpGetter(ILogger l, HttpClient http) : ContentGetter(l)
{
    public override async Task<StreamSpreader?> GetContentDataAsync(PlatformResult r, CancellationToken ct)
    {
        var spreader = new StreamSpreader();
        _ = (await http.GetStreamAsync($"/content?id={r.ID}", ct)).CopyToAsync(spreader, ct)
            .ContinueWith(_ => spreader.CloseAsync(), ct);
        return spreader;
    }
}
```

`PlatformResult.Downloaders` is already `[JsonIgnore]` (`PlatformResult.cs:8`), so results are
already nearly serializable.

ID-prefix routing comes from config, not a startup fetch:

```
Platforms:0:Url = http://gaida-youtube    Platforms:0:Ids = yt://,yt-playlist://
Platforms:1:Url = http://gaida-local      Platforms:1:Ids = audio://
```

### 3. Extract Local — `Gaida.Pods.MusicDatabase`

`STORAGE`, `ALBUM_COVERS` and the 3671-file library move with it. Now `AudioManager` has no
in-process platforms: collapse it to a URL list, a prefix map and `.Guarded()` per platform
(`AudioManager.cs:89`), and drop the `Gaida.Core.Platforms` namespace from Gaida.API.

Four concrete-type calls have to go first:

| Site | Now | After |
|---|---|---|
| `Content.cs:91,95` | `GetPlatform<YouTube>()`, `GetPlatform<MusicDatabase>()` | `/random` fan-out |
| `Content.cs:119` | `result is MusicResult` for the extension | relay the pod's `Content-Type` |
| `Query.cs:70` | `GetPlatform<MusicDatabase>().FindLocalVariant` | forward to `/variant` |
| `Browse.cs:25`, `Artist.cs:20,31` | `GetPlatform<…>()` | forward by route |

### 4. Replace `QueryParser` with `/classify` fan-out

`QueryParser.cs` is 122 lines of `audio://`, `yt://`, `yt-playlist://` (`:26-35`), YouTube host
matching (`:88-94`), the 11-char video-ID regex (`:117`) and the
`PL`/`UU`/`LL`/`RD`/`FL`/`WL`/`OLAK5uy_` prefixes (`:96-103`). It already duplicates
`SearchIDIdentifiers` (`YouTube.cs:36-37`, `MusicDatabase.cs:22`) and `IsPlaylistUrl`
(`YouTube.cs:72-75`). Left alone, adding Gaida.Pods.Spotify means editing the proxy — the exact
thing this refactor is meant to stop.

Fan out `/classify`, first claim wins, nobody claims → keyword search (the one classification rule
that stays in Gaida, because "unclaimed means search" is proxy-level knowledge). The platform
returns its own error string, so the specific messages in `API.md` survive.

`/Audio/FindQueryType` runs once per typed query, not per playback, so the fan-out is affordable.

Deletes `QueryParser`, and `SearchIDIdentifiers` / `SearchPlaylistIdentifiers` / `IsPlaylistUrl`
stop being Gaida's business.

### 5. `Stream` in, `Stream` out — `FFmpegEncoder.cs`

```csharp
public static async Task EncodeAsync(Stream source, Stream destination, string args, CancellationToken ct)
{
    using var p = Process.Start(new ProcessStartInfo("ffmpeg", $"-v quiet -nostats -i - {args} pipe:1")
        { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false })!;

    // ffmpeg only flushes its last packets once stdin reaches EOF, so the close has to happen even
    // when the copy above failed — otherwise the process sits holding the tail of the encode.
    var feed = source.CopyToAsync(p.StandardInput.BaseStream, ct)
        .ContinueWith(_ => p.StandardInput.BaseStream.Close(), ct);

    await p.StandardOutput.BaseStream.CopyToAsync(destination, ct);
    await feed;
}
```

Then delete from `Content.cs`: `StreamToResponse` `:300-360`, `StartEncode` `:216-230`,
`BufferedRangeResponse` `:272-283`, `SetRangeSupport` `:256-259` and both
`Closed && Range.Count > 0` branches (`:122`, `:160`). Gaida never holds a finished buffer, so
ranges are Dunav's. Keep `SetCacheHeaders` `:261-265` — the immutable+ETag pair is what lets Dunav
and anything above it key correctly.

### 6. Dunav

Almost entirely moved code, with its bug-history comments intact:

| Dunav needs | Lift from | Change |
|---|---|---|
| one fetch per key, racers await it | `ManagerService.cs:51-74` (`GetOrStartEncoderAsync`) | `Func<FFmpegEncoder,…>` → `Func<StreamSpreader,…>` |
| expiry sweep | `ManagerService.cs:95-109` / `AudioManager.cs:162-199` | pick one, they are the same loop |
| response pump | `Content.cs:300-360` | verbatim |
| 206 off a finished buffer | `Content.cs:272-283` | verbatim — **keep the `Closed` guard and its comment** |
| cache key | `ManagerService.cs:37-40` (`EncoderKey`) | verbatim |
| `/Audio/Preload` | `Content.cs:171-199` | `out started` already gives the 202/200 split |

Then delete from Gaida.API: `AudioManager.cs:14-22,25`, `:42-46`, `:122-199`; `ManagerService.cs`
`:15-16`, `:27-28`, `:32`, `:37-40`, `:51-109`; `Content.Preload` `:171-199`.

Dunav must store the **response headers** alongside the bytes — `Content-Type`,
`Content-Disposition`, `ETag`. A hit that replays bytes with a default content type breaks playback
in browsers.

Dunav project-references `Gaida.Core` for `StreamSpreader`/`StreamSubscriber`/`StreamStatus` only.
It never sees a `Platform`.

### 7. One public origin, routed by path — the admin's proxy

`DiscoveryContracts.cs:66` builds `contentUrl` from `PublicApiBaseUrl`
(`https://api.gergov.bg`), so without routing every search result the frontend plays bypasses
Dunav.

Rather than splitting the base URL in config, route at the edge — which is needed for TLS anyway:

| Path | → | Requirement |
|---|---|---|
| `/Audio/Download*`, `/Audio/Preload*` | `dunav` (5341) | `proxy_buffering off` |
| `/Multiplayer*` | `selo` (5342) | upgrade headers, long read timeout |
| everything else | `gaida-api` (5340) | — |

`DiscoveryResultMapper` keeps emitting one base URL and no `ContentBaseUrl` is needed.

The proxy is **not** shipped here — `compose.yaml` binds those three to loopback and the admin puts
their own in front. `nginx.example.conf` is a worked example. Two consequences worth stating:

- Routing `/Audio/Download*` at `gaida-api` instead of `dunav` silently removes coalescing. When
  nothing was published this was true by construction; with loopback ports it is a rule the proxy
  config has to keep. This is the one place the deployment can quietly undo phase 6.
- A proxy that buffers responses defeats progressive streaming entirely — the track downloads fully
  before the client hears anything.

## Coalescing is Dunav's job now

Stateless Gaida means N concurrent identical requests spawn N ffmpeg processes and N upstream
fetches. The `Lazy<Task<FFmpegEncoder?>>` in `GetOrStartEncoderAsync` is the only thing preventing
that today. Dunav restores it — **but only for traffic that goes through Dunav.**

So phase 7 is not optional polish; it is what makes phase 6's deletion safe. No coalescing needs to
be kept in Gaida once the edge routing is in, which is what keeps the deletion clean.

Note that "no cache" and "no coalescing" are separate properties. If Gaida ever has to be publicly
reachable again, keep the `Lazy` dedup and drop only the 45-minute retention.

## Selo

Multiplayer touches audio in exactly one place: `Room.cs:135`, `SearchID(id)`. Everything else — the
clock, the loading barrier, chat, sockets — is self-contained. `VirtualPlayer.Items` is
`List<PlatformResult>` but only ever needs id/name/artist/duration, so it becomes
`List<SearchResultDto>` and the coupling drops to one HTTP call.

Benefit today: a Gaida deploy stops killing every live room.

Constraint: rooms are an in-memory `ConcurrentDictionary` (`MultiplayerManager.cs:9`) with sticky
WebSockets. Single replica, or sticky ingress. Not a blocker, but it is not a scale-out pod.

## Where "pure proxy" leaks — honestly

**`Content.RandomResults` `:86-99`.** Randomized-rounding YouTube share, local backfills the
shortfall, shuffle. Real cross-platform composition, and it names YouTube. But `youTubeShare` is
already a public query parameter, so the name is in the contract regardless of topology. Move the
share to config (`Random:Shares:{youtube:0.4}`) and keep the param as an override. One platform name
in one config key is as pure as this gets without a contract change.

**`Cover.cs`.** Has its own disk cache (`:31-34`, `THUMBNAIL_CACHE`). Small files, disk-backed,
nothing to do with the spreader. Leave it in Gaida for now; it moves to the platform pods naturally
if artwork ever needs to be per-platform.

**`DiscoveryResultMapper`.** Stays in Gaida and should — owning the public shape and rewriting
`contentUrl` is exactly a proxy's job.

## Single host, Docker Compose — no orchestrator

**Decided.** See `compose.yaml`. Kubernetes was dropped: four of the five services are
single-instance by construction, so there is nothing to schedule.

| Service | Replicas possible |
|---|---|
| Gaida.Pods.MusicDatabase | 1 — pinned to `/nvme0` |
| Gaida.Pods.YouTube | 1 — one egress IP; more replicas just hit YouTube's rate limit sooner |
| Dunav | 1 — in-memory spreader; replicas split the cache and *raise* total RAM |
| Selo | 1 — in-memory rooms, sticky WebSocket |
| Gaida.API | N — stateless after phase 3 |

The split was never for scaling. It is for fault isolation (YouTube's rate limits must not take
local search down) and deploy independence (`docker compose up -d gaida-api` leaves Selo's rooms
alive). Both are process-boundary wins that Compose delivers in full.

Three k8s caveats disappear outright: node-pinning becomes a bind mount, Selo's sticky ingress
becomes one instance behind no load balancer, and "not publicly routable" becomes the default —
only Caddy publishes ports.

`Program.cs:18` pushes config into process environment variables for the platform layer. Each pod
keeps its own copy of that line for its own keys; Compose sets them directly.

### When load does increase

Capacity is **unique `(codec, bitrate, id)` per minute**, not listeners — a cached playback costs
no CPU, and ten people on one track in a Selo room is one encode. Measure with
`time ffmpeg -v quiet -i track.flac -c:a libopus -b:a 128k -f ogg /dev/null`; cores ÷ that × 60 is
the sustainable cold-start rate.

In the order things break:

1. **Dunav RAM.** `ExpireTimeSpan` is 45 min (`AudioManager.cs:21`) with no size bound, and
   `StreamSpreader.Data` (`StreamSpreader.cs:6`) is an unbounded list. Fix with an LRU byte
   ceiling (`Dunav__MaxBytes`), then by spilling completed encodes to disk — reuse the
   write-temp-then-`Move` pattern in `Cover.cs:108-136`. After that the RAM ceiling is concurrent
   *cold starts*, not catalog size, and finished encodes are just immutable files Caddy can serve.
2. **ffmpeg CPU.** `docker compose up -d --scale gaida-api=4`. Docker's DNS round-robins the
   service name, but `SocketsHttpHandler` pools connections against a 600s TTL and will pin to one
   replica — set `PooledConnectionLifetime = TimeSpan.FromMinutes(2)` on Dunav's client or the
   extra replicas get no traffic.
3. **YouTube rate limits.** Not a replica problem. The lever is `YouTubeCacher` hit rate.

`/Audio/Preload` is load-smoothing, not a nicety: it moves the encode for track N+1 into track N's
playtime, which is what stops a Selo room from stampeding at every track boundary. Do not lose it
in the phase 6 move.

Instrument before scaling anything: Dunav hit rate and resident bytes, concurrent ffmpeg count,
YouTube 429 rate.

**One free decision now:** key Dunav's entries as `{codec}-{bitrate}-{sha256(id)}` — valid as a
filename — so the disk-spill step is a fallback branch rather than a redesign.

## Deliberately skipped

- **Capability negotiation / service discovery.** A config block of URLs covers two or three pods.
  Add discovery when the list stops fitting in one.
- **Dunav proxying non-content routes.** WebSocket forwarding for no benefit.
- **A shared or persistent store behind Dunav.** It is the same in-memory dictionary you have now,
  just in its own pod. Add persistence when a Dunav restart during peak actually hurts.
- **Splitting `Gaida.Core` into an SDK assembly.** Stop referencing it from Gaida.API; leave the
  files where they are.
- **Any orchestrator** — k8s, k3s, Swarm, Nomad. Nothing to schedule while four of five services
  are single-instance and one host holds the library. Revisit only when a second host exists and
  something needs placing on it. `.NET Aspire` is the one worth a look if cross-service tracing
  becomes the reason (it generates the compose file and injects
  `services__gaida-youtube__http__0`-style discovery), but it is an SDK plus an AppHost project to
  replace ~40 lines of YAML — not worth it for the YAML alone.
