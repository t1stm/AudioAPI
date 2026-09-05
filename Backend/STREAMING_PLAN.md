# Streaming discovery responses end to end

**Status: implemented.** Three things went differently from the plan as drafted; all are marked
**Changed:** below. The largest is phase 1: the hand-written JSON writer the plan built its whole
approach on turned out to be unnecessary, and was deleted.

Companion to `AudioFrontend/HOME_LOADING_PLAN.md`. That plan makes the client render slot by slot as
JSON objects arrive; this one makes the API actually produce them that way. The client is correct
either way — with a buffered response it simply fills every slot at once, which is today's behaviour.

## What already streams, and what throws it away

The platform layer was written for this. `ISupportsSearch`, `ISupportsPlaylist` and
`ISupportsRandomResults` all return `IAsyncEnumerable<PlatformResult>`, `AudioManager.SearchKeywords`
and `SearchPlaylist` yield per item, and `Streaming.Guarded` already exists so one failing provider
ends its own sequence instead of the whole search. Nothing below needs a new abstraction.

Everything is then buffered three times on the way out:

| Layer | Where | What it does |
| --- | --- | --- |
| Pod | `Gaida.Pods.YouTube/Program.cs:104` `Collect`, `Gaida.Pods.MusicDatabase/Program.cs` `/search`, `/random`, `/artist` | drains the whole `IAsyncEnumerable` into a `List<ResultDto>` before `Results.Ok` |
| Gaida.API → pod | `Gaida/Gaida.Core/Platforms/HttpPlatform.cs:190` `FetchList` | `GetAsync<List<PodResultDto>>` — reads the pod's body to the last byte, then yields |
| Gaida.API → client | `Content.Search`, `Content.RandomResults`, `Artist.MapAndOrder`, `Query.ResolvePlaylist` | builds `List<SearchResultDto>`, returns `Ok(list)` |

So the first byte reaches the browser only after the slowest platform has finished. For a YouTube
keyword search that is one page fetch; for a 300-track YouTube playlist it is three round trips to
YouTube; for the Spotify flow described below it would be hundreds of searches.

### Per producer

| Producer | Streams per item in process? | What end-to-end streaming buys |
| --- | --- | --- |
| `YouTubeSearchProviderExplode.SearchKeywords` | yes — `Client.Search.GetVideosAsync` pages, capped at 15 | first hits render while the page is still being read |
| `YouTubeSearchProviderExplode.SearchPlaylist` | yes — `GetVideoBatchesAsync`, 100 per batch | **large.** A long playlist currently pays every batch before anything renders |
| `YouTube.GetRandomResults` | list from `YouTubeCacher.GetRandomAsync` (in memory) | small: overlapped serialisation only |
| `MusicSearchProvider.*` (`SearchKeywords`, `GetRandomResults`, `GetArtistSongs`) | `IEnumerable.AsAsync()` over an in-memory scan | small per item, but `count=200` and prolific artists are the two heaviest payloads on the home page |
| `AudioManager.SearchKeywords` | yes, platform after platform | **large.** Local results are ready in milliseconds and today wait for YouTube |
| `Spotify` | not implemented — `ISupportsID` only | **the reason for this plan.** See phase 5 |

`Gaida.Bot` and `Gaida.CLI` hold an `AudioManager` in process and already consume the async
sequences; nothing there changes. `Dunav` sits on `/Audio/Download*` only, so no discovery response
passes through its cache. `Selo/Multiplayer/Room.cs:113` reads `/Audio/Search` with
`GetFromJsonAsync<SearchResultDto[]>`, which keeps working: a chunked JSON array is still a JSON
array.

## The shape of the fix

Write the array incrementally at every hop, flushing per item, and read it incrementally at every
hop. `System.Text.Json` has both halves in the box (`DeserializeAsyncEnumerable`), so the only thing
written by hand is the writer, because the serialiser's own flush threshold (16 KB) would hold ~200
of these records back to the end of the response.

Three ordering decisions fall out of it, and they are the only real design work here:

- `RandomResults` shuffles the finished list. A stream cannot be shuffled at the end.
- `Artist.MapAndOrder` sorts by artist, then name, then id — documented in `API.md`.
- `FindQueryType` wraps playlist entries in an envelope (`kind`, `playlistId`, `results`).

Each is handled in its phase below.

## Phase 1 — nothing to write

**Changed: the plan was wrong here, and the code it describes was deleted.** It claimed MVC buffers an
`IAsyncEnumerable` returned from an action (`AsyncEnumerableReader`,
`MvcOptions.MaxIAsyncEnumerableBufferLimit`) and that System.Text.Json's 16 KB buffer would hold small
records back either way, and built a `WriteJsonArrayAsync` on top of both claims.

Measured on .NET 10 instead of assumed, with five records yielded 500 ms apart:

| Shape | Result |
| --- | --- |
| MVC action returning `IAsyncEnumerable<T>` | one 65-byte chunk per element, 500 ms apart, `Transfer-Encoding: chunked` |
| MVC action returning `Ok(sequence)` (`ObjectResult`) | same |
| Minimal API returning the sequence | same |
| Minimal API returning `Results.Ok(sequence)` | same |
| Minimal API, 30-byte records | same — the buffer threshold does not hold small elements back |
| `Ok(sequence)` on an action that also returns 400 | the 400 is still a 400, written whole |

So ASP.NET already does the whole job, at every hop, in both hosting models. Everything below is
returning the sequence instead of collecting it — no writer, no `Response.Body`, no `EmptyResult`.

`Gaida.Core/Utils/Streaming.cs` keeps only what ASP.NET does *not* do: `Guarded` (already there),
`RandomMerge` and `SelectParallel`.

The one property worth keeping from the deleted design is its consequence, and it holds either way:
**the status code is settled before the first element.** Once the array is open a failure can only
truncate it, so every validation has to happen before the sequence is handed over.

## Phase 2 — pods return the sequence

`Gaida.Pods.YouTube/Program.cs`, `Gaida.Pods.MusicDatabase/Program.cs` and `Gaida.Pods.Spotify`, on
`/search`, `/playlist`, `/random` and `/artist`. `Collect` is replaced by a `Mapped` iterator that
maps as it goes:

```csharp
app.MapGet("/search",
    IResult (string? q, CancellationToken ct) => string.IsNullOrWhiteSpace(q)
        ? Results.Ok(Array.Empty<ResultDto>())
        : Results.Ok(Mapped(youTube.SearchKeywords(q, ct), ct)));

static async IAsyncEnumerable<ResultDto> Mapped(IAsyncEnumerable<PlatformResult> source,
    [EnumeratorCancellation] CancellationToken ct)
{
    await foreach (var result in source.Guarded(Log.Logger, nameof(YouTubePlatform), ct))
        if (ResultMapper.Map(result) is { } dto)
            yield return dto;
}
```

`Guarded` stays exactly where `Collect` had it, so a provider that throws halfway still ends the array
cleanly rather than tearing the response.

`/browse` and `/variant` keep returning whole objects. `/browse` is one directory level and `/variant`
is a single match; neither has a per-item wait to hide.

## Phase 3 — `HttpPlatform` reads it

`FetchList` (`Gaida/Gaida.Core/Platforms/HttpPlatform.cs:190`) is the one place Gaida.API talks to a
pod, so this single change unbuffers `/search`, `/playlist`, `/random` and `/artist` at once:

```csharp
private async IAsyncEnumerable<PlatformResult> FetchList(string path,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    using var response = await _http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    if (!response.IsSuccessStatusCode) yield break;   // a pod without the route must not take the others down

    await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
    await foreach (var dto in JsonSerializer.DeserializeAsyncEnumerable<PodResultDto>(
                       body, jsonOptions, cancellationToken))
        if (dto is not null)
            yield return ToResult(dto);
}
```

`HttpCompletionOption.ResponseHeadersRead` is the half people forget: without it `HttpClient` buffers
the body before the first `await` returns and the async enumerator reads from memory.

The existing swallow-everything `try/catch` cannot wrap a `yield return`, so the failure handling
moves out to the call sites — which already have it: `AudioManager.SearchKeywords` and
`SearchPlaylist` wrap each platform in `.Guarded(...)`. The two that do not are
`Content.RandomResults` (`Gaida.API/Controllers/Content.cs:95-99`) and `Artist.GetArtistLocal`; both
get `.Guarded(...)` in phase 4. `GetAsync<T>` keeps its `try/catch` for `/resolve`, `/browse`,
`/variant` and `/classify`.

## Phase 4 — Gaida.API writes it

### One mapping iterator, two controllers

`DiscoveryStream.Mapped` maps `PlatformResult` to `SearchResultDto` as results arrive, and the actions
return `Ok(...)` over it. `[ProducesResponseType<IReadOnlyList<SearchResultDto>>(200)]` stays on them
so OpenAPI keeps describing the array; only the transport changes.

**Status codes must be decided before the first element.** Every validation these actions do already
happens up front — `count`/`youTubeShare` range checks, the empty-term guards, and `ClassifyAsync`,
which is awaited before any result is produced. Keep it that way: once the array is open, a failure
can only truncate it, exactly as `HOME_LOADING_PLAN.md` describes on the client side.

### `/Audio/Search`

Straight through. `manager.SearchKeywords` / `SearchPlaylist` already yield per platform per item, and
platforms are iterated in registration order, so putting the local pod first in `Platforms` config
means library hits paint while YouTube is still being asked. The `ID` branch stays as it is — one
result, nothing to stream.

### `/Audio/RandomResults`

The one endpoint whose behaviour changes. Today: fetch the YouTube share, backfill from local with
`count - results.Count`, shuffle the union, return. A stream has no "union" and no shortfall figure
until the sources are done.

Replace both with a random merge:

```csharp
// ponytail: local is an in-memory shuffle at the pod, so over-asking is free and the backfill
// arithmetic disappears — whatever YouTube is short of, local is already producing.
var youTube = manager.PlatformFor("yt://") is HttpPlatform yt
    ? yt.GetRandomResults(youTubeCount, token).Guarded(...) : AsyncEnumerable.Empty<PlatformResult>();
var local = manager.PlatformFor("audio://") is HttpPlatform db
    ? db.GetRandomResults(count, token).Guarded(...) : AsyncEnumerable.Empty<PlatformResult>();

await foreach (var result in RandomMerge(youTube, youTubeCount, local, count, token))
    ...
```

`RandomMerge` keeps one pending `MoveNextAsync` per source, picks between them with a probability
proportional to how many each still owes, drops a source when it ends, and stops at `count`.
Randomised rounding for `youTubeCount` stays exactly as it is (`Content.cs:86-89`). The result: the
same mix, arriving interleaved, without a terminal `Random.Shared.Shuffle`. It lives in `Content.cs`,
private — one caller, no reason for it to be in `Gaida.Core`.

Its test: with two synthetic sources, 1000 runs of `count=10, share=0.4` land within a sane band of
4 YouTube items on average, always return exactly 10, and return 10 with local backfilling when the
YouTube source yields nothing.

### `/Audio/Artist/Local`

`MapAndOrder` sorts by artist, then name, then id — a documented contract (`API.md:30`) and
incompatible with streaming. Move the sort into the local pod's `/artist` route: it holds the whole
library in memory (`MusicManager.GetArtistSongs` materialises a `List` anyway,
`Manager/MusicManager.cs:294`), so sorting there costs nothing and the documented order survives.
Gaida.API then maps and streams straight through, and the stale comment at
`Gaida.Pods.MusicDatabase/Program.cs:112` ("Ordering is Gaida.API's job") goes with it.

### `/Audio/Artist/YouTube`

This one is a keyword search wearing an artist endpoint's name, so the same alphabetical sort is
applied to what YouTube returned in relevance order. Streaming means dropping that sort. Dropped, streamed in
relevance order, `API.md` updated — a better answer for the caller than alphabetising 15 search hits.

### `/Audio/FindQueryType`

The playlist branch (`Query.ResolvePlaylist`) drains the whole playlist into an envelope. The envelope
is the problem: `kind` and `playlistId` are known immediately, `results` is the slow part, and a
half-written envelope is not something the client parser in `HOME_LOADING_PLAN.md` reads.

**Changed:** the plan was to drop `results` from the envelope and let clients stream the entries from
`/Audio/Search?query={canonical playlist url}` instead. `results` was kept. The frontend reads it
today (`src/requests/songs.ts:92`), so dropping it would break the paste-a-link flow the moment this
deployed, for a call that runs once per paste rather than on every page load. The streaming route
exists and is documented in `API.md` as the one to prefer for long playlists — `Search` already
routes a playlist claim to `SearchPlaylist` — so the field can go once the frontend has moved off
it.

## Phase 5 — Spotify, the case this is for

> **Since shipped.** The pod was rebuilt in Python on [SpotAPI](https://github.com/Aran404/SpotAPI),
> which reaches Spotify's own web endpoints: `SPOTIFY_ID` / `SPOTIFY_SECRET` are gone, and so is
> `Gaida.Platforms.Spotify` — the C# platform library and its `SpotifyAPI.Web` dependency were deleted
> along with the Bot's in-process registration of it. `/search` is now implemented rather than `404`,
> which is why `SelectParallel` no longer buffers a window for results that need no lookup and why
> `/Audio/Search` deduplicates by ID. Everything below is the design as first shipped.

Spotify is half-built: `Gaida.Platforms.Spotify` implements `ISupportsID` only, is not wired into any
pod, and its `SpotifyResult.GetDownloadUrl()` returns `""`. That last one is the whole design
constraint — **Spotify results are never playable**. They are names. The playable track has to come
from a second search, per track, against the platforms that do have content.

### Pod

`Gaida.Pods.Spotify`, mirroring the other two: `/classify` claims `open.spotify.com` URLs, `spotify://`
and `spotify-playlist://`; `/resolve` is the existing `GetByIdAsync`; `/playlist` streams
`Playlists.GetItems` page by page (100 per page — the same batch shape as YouTube's playlists);
`/search` maps to Spotify's search; `/random` and `/content` are `404`, which `HttpPlatform` already
treats as "not supported". `SPOTIFY_ID` / `SPOTIFY_SECRET` come in as environment variables like every
other pod's config, and an unconfigured client keeps its current behaviour: log once, return nothing.

### The resolver, in Gaida.API

**Changed:** the Spotify platform implements `ISupportsID` and `ISupportsPlaylist` only, and the pod
answers `404` on `/search`. A keyword query already fans out to the platforms that have audio, so a
Spotify hit for one would only be another name to resolve into the result they had just returned.
That also keeps the resolver off the ordinary search path, where `SelectParallel`'s window would hold
the first result back until four had arrived.

Each Spotify result is turned into a playable one by searching for `"{artist} {name}"` — the local
pod first (its `/variant` route is exactly this question, name + artist + duration), then YouTube's
keyword search, first hit wins. That is one or two pod round trips per track, ~100–500 ms each; a
50-track playlist is half a minute of work whose first result is ready in well under a second.
Without streaming the caller waits for the whole thing. With it, the playlist fills track by track.

Marking which pods need it: the pod config in `Gaida.API/appsettings.json` already carries `Url` and
`Ids` per platform, so add `Resolve: true`. No new interface, no per-result flag on the wire.

Order matters here — it is a playlist — so the lookups run in parallel but are emitted in the
playlist's own order, with a helper in `Gaida.Core/Utils/Streaming.cs`:

```csharp
/// <summary>
///     Runs <paramref name="selector"/> over the source with at most <paramref name="concurrency"/>
///     in flight, yielding results in source order. Nulls (nothing found) are dropped.
/// </summary>
public static async IAsyncEnumerable<TOut> SelectParallel<TIn, TOut>(this IAsyncEnumerable<TIn> source,
    int concurrency, Func<TIn, CancellationToken, Task<TOut?>> selector,
    [EnumeratorCancellation] CancellationToken cancellationToken = default) where TOut : class
{
    var pending = new Queue<Task<TOut?>>(concurrency);
    await using var enumerator = source.GetAsyncEnumerator(cancellationToken);

    while (pending.Count < concurrency && await enumerator.MoveNextAsync())
        pending.Enqueue(selector(enumerator.Current, cancellationToken));

    while (pending.Count > 0)
    {
        var result = await pending.Dequeue();
        if (await enumerator.MoveNextAsync())
            pending.Enqueue(selector(enumerator.Current, cancellationToken));
        if (result is not null) yield return result;
    }
}
```

Concurrency of 4 to start with, configurable next to the pod entry. Higher risks YouTube rate limits,
which are the actual ceiling here, not CPU.

Its test: a source of 20 items whose selector completes in deliberately reverse order, asserting
output order equals input order, that no more than `concurrency` selectors are ever in flight at once,
and that a `null` result is skipped rather than yielded.

Also worth having, once this exists: the same resolver behind a `resolve=true` flag on `/Audio/Search`
would let any metadata-only platform (Apple Music, Deezer, a pasted tracklist) reuse it. Not building
it now — one platform, one caller.

## Phase 6 — the transport, where streaming quietly dies

- **nginx** (`nginx.example.conf:63`): the `/Audio` block has no `proxy_buffering off`, so nginx will
  hold the whole discovery response exactly as it used to hold the whole encode — the same 1.9 s
  lesson the `/Audio/Download` block already carries a comment about. Add `proxy_buffering off;` to
  the `/Audio` block. `proxy_http_version 1.1` is already in the shared include, which is what makes
  chunked transfer possible at all.
- **gzip**: leave it on and measure. nginx compresses incrementally, and 200 records compress well; if
  the network panel shows the body arriving whole, that is the next thing to turn off for these paths.
- **No response compression or output caching middleware** in Gaida.API or the pods, and none exists
  today — keep it that way for these routes.
- **Kestrel** chunks automatically once no `Content-Length` is set, which is what writing to
  `Response.Body` gives.
- **Discord's `/.proxy/` path** may buffer regardless, as `HOME_LOADING_PLAN.md` notes. Degraded
  behaviour is today's behaviour, so it is a measurement, not a blocker.
- **Timeouts**: `proxy_read_timeout 90s` on `/Audio` is a between-bytes gap, not a total, so a long
  Spotify resolve is safe as long as tracks keep landing. A playlist whose every lookup fails would
  produce a long silent gap — the resolver yields nothing for a miss. Worth watching; if it bites,
  the fix is to emit the unresolved track rather than nothing.

## Documentation

- `API.md`: note that the discovery endpoints respond `Transfer-Encoding: chunked` with elements
  written as they are produced, that the array is still an ordinary JSON array for buffered clients,
  that a mid-stream failure truncates the array rather than changing the status code, the
  `/Audio/Artist/YouTube` ordering change, and the `FindQueryType` playlist envelope change.
- `HANDOVER.md`: the verified-routes table.

## Verification

- `dotnet build`, the `--self-check` runs for `Gaida.API` and all three pods, `dotnet test` including
  the two new tests in `StreamingTests.cs` (`RandomMerge`, `SelectParallel`).
- `curl -N --raw -s -D- 'http://localhost:5000/Audio/RandomResults?count=200'` — expect
  `Transfer-Encoding: chunked`, no `Content-Length`, and objects appearing over time rather than in
  one burst. Repeat against the pod directly to isolate which hop buffers if it does not.
  Done against a live YouTube pod and a Gaida.API in front of it: `/search` and `/Audio/Search` both
  answer `Transfer-Encoding: chunked` with no `Content-Length`, 15 real results, no errors in either
  log, and `count=999` still answers `400`. Per-element arrival was measured on a probe app (the table
  in phase 1); against YouTube itself every source tried produced its results in one upstream batch,
  so end-to-end per-track arrival is still the manual pass below.
- The same through nginx, before and after the `proxy_buffering off` line, to confirm which layer was
  holding the body.
- With the frontend's phase 4 in place: `/RandomResults?count=30` fills the picks row slot by slot,
  and a 200-track YouTube playlist pasted into the search box fills progressively.

## Out of scope

- Reducing the work itself: the 200-track random fetch exists only to tally artist names, and an
  endpoint returning artist counts would beat streaming it. `HOME_LOADING_PLAN.md` says the same from
  the other side.
- `/Audio/Browse`, `/Audio/Cover`, `/Audio/Local/Variant`, `/Audio/DownloadRaw` — single objects or
  already streaming.
- Server-sent events or WebSockets for discovery. A chunked JSON array needs no client library, no
  reconnect logic, and no second content type; `Selo` is where the socket belongs.
