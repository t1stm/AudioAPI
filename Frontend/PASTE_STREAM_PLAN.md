# The paste box that fills the queue while you watch

> **Status:** implemented.
>
> Backend proof: `dotnet build` and `dotnet test` (95 in Gaida.Tests) pass. Frontend proof:
> `npm test` (126, one of them new), `npm run lint` and `npm run build` pass. Not yet driven against
> a live API — the stack was not running.
>
> **Changed from the draft:** `FindQueryType` does not take an `entries` parameter. The ability to
> get a whole playlist out of it is retired instead: `results` is gone from the contract, and so is
> `Query.ResolvePlaylist`. One endpoint answers what a pasted value *is*, one endpoint streams
> tracks, and neither pretends to do the other's job — a parameter would have kept the slow path
> alive for whoever forgot to pass it.
>
> Scope: one repository, both halves. `Frontend/` for the interaction, `Backend/` for retiring the
> envelope the client was waiting on.
>
> The problem in one line: paste a Spotify playlist, and the box says "Resolving…" for as long as
> the API takes to look every track up on YouTube — one lookup per track, a few at a time — and
> then the whole playlist appears at once, with no way to stop it.

---

## 1. What happened before this

`resolvePaste` was one await:

```ts
const resolved = await findQueryType(value);      // blocks on the whole playlist
queue.playNow(resolved.results[0]);
for (const track of resolved.results.slice(1)) queue.add(track);
```

`findQueryType` hits `/Audio/FindQueryType`, whose playlist branch
(`Backend/Services/Gaida.API/Controllers/Query.cs`, `ResolvePlaylist`) collects every entry into a
`List<SearchResultDto>` before returning, because the entries live inside an envelope with `kind`
and `playlistId` and a half-written envelope is not readable. Its own doc comment, and `API.md`
line 65, already name the way out:

> For a long playlist, send the canonical `query` back to `Search`, which routes it to the same
> lookup and streams the entries as they resolve.

So the streaming half of this plan is a route that already exists. `/Audio/Search` classifies,
routes a playlist claim to the same `SearchPlaylist` + `PlayableResolver` pipeline, and returns an
`IAsyncEnumerable` that ASP.NET flushes element by element. On the client, `streamResults`
(`src/requests/songs.ts:73`) already reads that with `streamJson`, and the search page already
renders from it as it arrives. Nothing new has to be built to make tracks arrive one at a time.

What is missing is only this: the client cannot ask *what kind of query this is* without also
paying for the full resolution, because the one endpoint that answers `kind` is the one that
assembles the envelope.

---

## 2. The one backend change

A playlist claim answers with its identity and nothing else. `Query.ResolvePlaylist` — the
`await foreach` that drained the playlist into a `List<SearchResultDto>` — is replaced by a static
`PlaylistResolution`, which is the two lines that used to sit *below* that loop:

```csharp
private static QueryResolutionDto PlaylistResolution(string playlistUrl)
{
    var spotify = playlistUrl.StartsWith("spotify-playlist://", StringComparison.OrdinalIgnoreCase);

    return new QueryResolutionDto
    {
        Kind = spotify ? "spotifyPlaylist" : "youtubePlaylist",
        Query = playlistUrl,
        PlaylistId = spotify ? playlistUrl["spotify-playlist://".Length..] : ExtractPlaylistId(playlistUrl)
    };
}
```

`Results` comes off `QueryResolutionDto` with it. Everything in the answer was already known the
moment classify returned, so the response now costs one `/classify` fan-out and no lookups.

`ID` and `search` claims are untouched: an ID resolution is a single lookup, which is the work the
caller actually wants done, and a `search` answer never carried entries.

`API.md` loses `results` from both playlist rows and from the example, and gains the paragraph that
says the canonical `query` is the only route to a playlist's tracks. `STREAMING_PLAN.md` had this
change written down already, deferred because the frontend still read the field; its note now says
it landed.

**Why not skip `FindQueryType` altogether and just stream `/Audio/Search` with the raw text?**
Because `Search` answers with results, not with a kind, and the client's three outcomes are not the
same shape: ordinary text navigates to the search page, a single ID plays, a playlist fills the
queue. Guessing the kind from the shape of the pasted string would move classify — which lives in
the platform pods, on purpose — into the browser.

---

## 3. The flow, after

```
paste ──▶ FindQueryType ──▶ kind
                            │
   search ───────────────── ┼──▶ goto /search?term=…   (unchanged)
   local / youtubeVideo ─── ┼──▶ queue.playNow(result) (unchanged, one row on the tape)
   youtubePlaylist ─────────┤
   spotifyPlaylist ─────────┴──▶ streamSearch(resolved.query, signalled fetch)
                                     │
                                     ├─ first track ─▶ queue.playNow ─▶ tape row, "now" in gold
                                     ├─ every other ─▶ queue.add     ─▶ tape row
                                     └─ Cancel      ─▶ controller.abort()
```

In `resolvePaste`:

```ts
const controller = new AbortController();
pasteAbort = controller;
// signal threaded through the Fetcher both request helpers already take — no signature change
const signalled: typeof fetch = (input, init) => fetch(input, { ...init, signal: controller.signal });
```

`findQueryType` and `streamSearch` both already take a fetcher, so `signalled` goes straight in and
nothing in the request layer changes shape. `signalled` is typed `typeof fetch` rather than spelling
the parameters out — shorter, and it is exactly the shape both helpers want.

Cancellation semantics, stated because they are a product decision and not an implementation
detail:

- **Cancel stops the stream; it does not undo the queue.** Tracks that already landed are playing
  or queued, and yanking them back out from under a track that is already playing is a worse
  surprise than a short playlist. The status line says so: `Stopped. 12 tracks added.`
- Aborting closes the response, so `HttpContext.RequestAborted` fires server-side and the pods stop
  looking up the remaining tracks. Cancel is real work saved, not just a hidden spinner.
- An abort is not an error. `signal.aborted` in the catch means the status line, not `pasteError`.
- In a room, `queue.add` becomes `add <id>` over the socket, so every listener sees the playlist
  arrive track by track too — the same change, for free.

An empty playlist still ends with zero rows and keeps today's message: *That playlist did not
contain any playable tracks.*

---

## 4. Design direction

The app has a palette, three faces and a vocabulary; this brings no new colour and no new font.
The design work here is choreography, not identity — the panel is one of the quietest on the page
and should stay that way when idle.

### What the existing system already says

| Token / idiom | What it already means | Where |
| --- | --- | --- |
| `.rain-streak` | **the wait is named, not hidden** — violet falling down a column | seek bar's buffer gauge |
| `.eyebrow` (JetBrains, 0.68rem, 0.13em) | a structural label, machine-measured | every section head |
| `.font-mono` + `tabular-nums` | facts the machine measured; digits line up | durations, counts |
| `gold` | *the library has this* | variant tag, library search heading |
| `ember` | the destructive / interrupting side of a fork | rendition tag |
| `radius-panel 8 → row 6 → art 4` | panels contain rows contain artwork | everywhere |

### The signature: the tape

While a playlist resolves, the panel grows a third element — a narrow column of one-line rows, one
per track, newest at the bottom, with **a `rain-streak` running down its left gutter** for as long
as the stream is open. The app is called musicrain and its existing "still filling" element is
literally rain falling down a column; here the tracks land underneath it. When the stream ends the
streak stops and the gutter goes to a static `haze` hairline.

```
┌──────────────────────────────────────────────────────┐
│ ⌁ ADDING TO THE QUEUE · 12                           │   eyebrow, count in mono
│ ┌──────────────────────────────┐ ┌────────────────┐  │
│ │ https://open.spotify.com/pl… │ │     Cancel     │  │   input stays, filled and dimmed
│ └──────────────────────────────┘ └────────────────┘  │   ember hairline, not filled
│ ┃                                                    │
│ ┃ 01  Kelly Lee Owens — Corner of My Sky      ● now  │   gold dot on the first
│ ┃ 02  Jockstrap — Greatest Hits               4:12   │
│ ┃ 03  Loraine James — Glitch the System       3:38   │
│ ┃ 04  …                                              │   the row still resolving
│ ▲ ← rain-streak, falling, while the stream is open   │
└──────────────────────────────────────────────────────┘
```

Numbering is not decoration here: a playlist **is** an ordered sequence, and the number is the
position the track just took in the queue. That is information the row cannot carry otherwise, and
it is the only reason to print it — a plain search result list would get no numbers.

Details:

- **Type.** Index and duration in `font-mono` (tabular figures make the column a column); name and
  artist in the body face, `chalk` for the name, `fog` for the artist, one line, truncated. The
  count in the heading is mono because it is a machine tally that changes twice a second.
- **Motion.** Each row enters on the page's existing `reveal` keyframe — the one the variant prompt
  and its tag already use — rather than a second entrance of its own. Nothing is staggered: the
  stagger is the network, and faking more of it is the tell. Under `prefers-reduced-motion: reduce`
  the row simply appears and the streak holds at `opacity: 0.35`, as `app.css` already does for it.
- **Height.** `max-h-56 overflow-y-auto`, pinned to the bottom as rows arrive, so a 200-track
  playlist does not push the rest of the page down the screen. When the stream ends, the tape stays
  until the next paste — it is the receipt.
- **Live region.** The heading count is `aria-live="polite"`; the tape itself is not, or a screen
  reader reads two hundred rows aloud.

### The button

The submit button swaps to **Cancel** for the duration, rather than a disabled "Resolving…" sitting
next to a second live button. One action slot, one action, and the label always names what pressing
it does — the same rule the play/queue fork on the hero follows. It is `type="button"`, ember
hairline on transparent, so the interrupting action never looks like the primary one.

*(For the literal "a new button shows up": keep the disabled submit and add Cancel beside it — one
extra element, no other change. Not done; a dead button next to a live one makes the user read
both.)*

---

## 5. Phases

1. **Backend.** `PlaylistResolution` replaces `ResolvePlaylist`, `Results` comes off the DTO,
   `API.md` and `STREAMING_PLAN.md` updated. `dotnet build`, `dotnet test`.
2. **Request layer.** `QueryResolution`'s playlist arm loses `results`, so `isPlaylist` guards on
   `playlistId`; the existing playlist test asserts an entry-less resolution still reads as a
   playlist, and a new one asserts an aborted `streamResults` throws rather than ending quietly —
   a cancelled playlist must not look like a finished one.
3. **The flow.** `resolvePaste` rewritten as above: `AbortController`, per-track `playNow`/`add`,
   abort-aware catch, `pasteTracks` state.
4. **The tape.** Markup, the streak gutter, the entry transition, the reduced-motion and live-region
   behaviour, the button swap.

Proof: `npm test`, `npm run lint`, `npm run build`, then driven in Chrome against the live API at
390 and 1440 — a Spotify playlist link (the slow case), a YouTube playlist, a bare `yt://` ID, and
plain text; Cancel pressed mid-stream, with the network panel showing the request closed and the
queue holding exactly the tracks the tape printed.
