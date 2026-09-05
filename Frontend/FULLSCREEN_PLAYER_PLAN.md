# The full-screen player, and the album line it needs

> **Status:** all six phases implemented.
>
> Backend proof: `dotnet build` and `dotnet test` (94 in Gaida.Tests) pass, and
> `dotnet run --project Gaida.Pods.MusicDatabase -- --self-check` prints `selftest OK`. Exercised
> live against a scratch library: a tagged .mp3 indexes as `"Album": "Duran Duran"`, `"Scan": 1`
> and serves `"album"` over `/search`; stripping both fields back out and reloading refills the
> album, stamps the scan and leaves the ID (`ducome-u-ee`) untouched.
>
> Frontend proof: `npm test` (125), `npm run lint` and `npm run build` pass. Driven in Chrome
> against the live API at 390, 430, 760×480 and 1440: the full shape opens and closes from the
> button, Escape and the back button, the queue button collapses it, and the audio element never
> restarts — `currentTime` ran straight through every open and close.
>
> Scope: one repository, both halves of it — `Frontend/` for the player, `Backend/` for the album
> tag the player is supposed to show and the library never read.
>
> Two problems, one plan. The player is a 53px bar with a 40px thumbnail in it, and there is no way
> to make the artwork the thing you are looking at. And every song in the library has
> `"Album": null`, because the tag reader was never asked for `ALBUM` — so the third line of the
> design below would be blank on every track in the database.

---

## 1. Design direction

The app already has a visual system, a palette and a vocabulary. This extends them; it brings no
new font and no new colour.

### What the existing system already says

| Token / idiom | What it already means | Where |
| --- | --- | --- |
| `#player-cover` | cover, full-bleed, `dark-0` at 74%, `blur(2px)` | micro-mode player, playlist hero |
| `#room-rail` | one pixel carries the room's state — haze / gold / violet | micro-mode player |
| `.rain-streak` + the gauge | **the wait is named, not hidden** | seek bar's buffer column |
| `.eyebrow` (JetBrains, 0.68rem, 0.13em, fog) | a structural label, machine-measured | every section head |
| `--font-display` Unbounded | the app's own name and page titles | header mark, playlist hero |
| `radius-panel 8 → row 6 → art 4` | panels contain rows contain artwork | everywhere |
| `micro:` (`max-height: 320px`) | the player *is* the interface | `app.css` |

The last one matters more than it looks. Micro mode is already a full-screen player, written as
plain id-anchored CSS over the same DOM the bar uses. It is proof that this player changes shape
without changing components — and it is the pattern the two new shapes follow.

### The one non-negotiable constraint

`<audio>` lives inside `Player.svelte`. A separate `FullScreenPlayer.svelte` would either mount a
second element or unmount the first, and both stop the music. **So the full-screen player is not a
new component. It is a third shape of `#player`**, reached by an attribute:

```
#player[data-shape='bar']   the 53px transport (today's default)
#player[data-shape='full']  this plan — mobile column, desktop two columns
@media (max-height: 320px)  micro, unchanged, and it outranks both
```

Nothing remounts. The seek bar keeps its position, the buffer gauge keeps its fill, the audio
graph is never rebuilt.

### The hero: the cover, twice

The cover appears twice at once — full-bleed as the ground at 74% dark and `blur(2px)`, and sharp
in the middle at its true aspect. That is exactly the `#player-cover` treatment micro mode already
paints, reused rather than re-invented, and it is why the screen is coloured by the record instead
of by a gradient.

### The signature: the rain climbs the edge

The buffer gauge is a 14×28px column beside the seek bar. At full screen that is a detail nobody
sees from a metre away — and this app's one firm opinion is that a wait gets named. So in the
`full` shape the gauge moves to the screen's left edge and becomes a 2px column the full height of
the display, filling toward the three-second runway with the same `.rain-streak` gradient falling
through it.

Buffering is then legible across the room, and it is the only moving thing on the screen. It is
also the app answering in water again, like the queue badge's droplet and the session strip's
rail. One accessory; everything else holds still.

### Typography: three registers, three facts

The screenshot stacks title, artist, album in one grey. This app can say more with the faces it
already loads:

| Line | Face | Why |
| --- | --- | --- |
| Title | `--font-display` Unbounded, 300, `tracking-tight`, `clamp(1.35rem, 5vw, 2rem)` | the one name a person chose to play |
| Artist | `--font-body` Golos, 400, 1rem, `text-chalk`, still an `<ArtistLink>` | a person, and a link to their page |
| Album | `.eyebrow` — JetBrains, uppercase, 0.13em, `text-fog` | a fact the file was tagged with |

Unbounded is deliberately *not* used in micro mode ("too wide to read at this size"). At full
screen there is width for it, and it is the only place in the player where the app's display face
appears — which is the point.

### Mobile

```
┌────────────────────────────────┐
│░ cover, blurred, 74% dark ░░░░░│  ← ground
│▌                            ⌄  │  ← rain column (only while buffering) · collapse
│                                │
│      ┌──────────────────┐      │
│      │                  │      │
│      │      cover       │      │  var(--cover) = min(78vw, 44vh)
│      │                  │      │
│      └──────────────────┘      │
│                                │
│          Come Undone           │  Unbounded 300
│          Duran Duran           │  Golos, links to /artist
│          DURAN DURAN           │  eyebrow — only if the tag exists
│                                │
│      ⤨    ≡+   ⌗   FLAC        │  shuffle · queue · chat · format
│                                │
│  0:02 ▬▬●───────────────  4:16 │  mono, tabular
│                                │
│       ⏮       ⏸       ⏭        │  56px targets, thumb height
└────────────────────────────────┘
```

Order follows the screenshot: artwork, then who it is, then what you can do about it, then where
you are, then transport last — nearest the thumb.

### Desktop

A phone layout stretched to 1400px is the thing to avoid. The desktop shape is two columns,
vertically centred, with the artwork given the whole left half and every word and control left-
aligned on a single axis in the right half:

```
┌──────────────────────────────────────────────────────────────────────┐
│░░ cover as ground, 74% dark ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  ⌄  │
│▌                                                                    │
│         ┌─────────────────────┐    Come Undone                      │
│         │                     │    Duran Duran                      │
│         │        cover        │    DURAN DURAN                      │
│         │   min(46vh, 40vw)   │                                     │
│         │                     │    ⤨   ≡+   ⌗   FLAC · 320   🔊     │
│         └─────────────────────┘                                     │
│                                     0:02 ▬▬●──────────────── 4:16   │
│                                     ⏮      ⏸      ⏭                 │
│                                                                     │
└──────────────────────────────────────────────────────────────────────┘
```

The volume slider appears here and only here — it is already `hidden sm:block` in the bar, and the
existing reasoning holds (on a phone the hardware keys own volume). The format picker opens
upward, as it does today; at full screen it has room to open downward instead — one `bottom-full`
→ `top-full` swap under the shape selector.

### Copy

| Where | Text |
| --- | --- |
| Expand button | `aria-label="Open the full player"` |
| Collapse button | `aria-label="Close the full player"` |
| Album line | the tag, verbatim, uppercased by CSS — no `Unknown album`, the line is absent instead |
| Shuffle | `aria-label="Shuffle what is coming up"` — it shuffles the upcoming tracks, not the queue |

No new visible words. A full-screen player that has to explain itself has already failed.

### Quality floor

- Every new control is a real button with `focus-visible:ring-2 ring-primary-500`, ≥44px on touch.
- The shape change is an attribute, so `prefers-reduced-motion` has nothing to suppress except the
  cover's size transition (§6) and the rain, which `app.css` already stills.
- Down to 320px wide: the cover is `78vw`, the transport row is three 56px targets in `1fr` each.
- Escape, the back button and the Android back gesture all close it — `closeOnBack`, like every
  other layer in the app.
- Nothing is announced twice: the title is already in `MediaMetadata`; the full player adds
  `album` to that record and nothing else.

### What this deliberately does not add

The reference screenshot carries a heart, an info button, an add-to-playlist button, a `⋯`, and a
repeat-one toggle. Three of those have no feature behind them here (no favourites, no track info
panel, no repeat mode) and would be dead buttons. Add-to-playlist exists but belongs to a row, not
to the thing already playing. The full player shows only controls that already do something.

---

## 2. The album tag (`Backend/`)

### Why every album is null

`MediaInfo.GetInformation` reads exactly two tags:

```csharp
musicInfo.Titles  = MusicInfo.Variants(Tag(tags, "TITLE"));
musicInfo.Artists = MusicInfo.Variants(Merge(Tag(tags, "ARTISTS")), Merge(Tag(tags, "ARTIST")));
```

`ALBUM` was never asked for. Everything downstream of it is already wired — `MusicInfo.Album`,
`MusicResult.Album`, the pod's `ResultDto`, `DiscoveryContracts`, `Selo`'s `SearchResultDto`,
`Dom`'s `TrackSnapshot`, and the frontend's `SearchResult.album`, which `SearchRow` already
prints. The admin panel can even set it by hand, and `MusicManager` counts `withoutAlbum` in its
stats. One missing line at the very top of the pipeline empties all of it.

### The fix

```csharp
musicInfo.Album = Tag(tags, "ALBUM")?.Trim() is { Length: > 0 } album ? album : null;
```

`Tag` is already case-insensitive, which is what makes this work for `album` from an .mp3 and
`ALBUM` from a .wv alike.

### The backfill, and the trap in it

New files pick the album up on their next scan. The library that is already indexed does not:
`NewFiles` never revisits an indexed song. The existing answer to that problem is `WasLegacy` —
entries in the old four-field format get `RereadTags` on load. This needs the same treatment with
a wider gate, and there is a trap in reusing that method directly:

```csharp
private static async Task RereadTags(MusicInfo entry)
{
    ...
    entry.ID = entry.UpdateRandomId();   // ← re-rolls a random suffix
```

`UpdateRandomId` ends in `RandomString(2)`. Re-reading every entry to fill an album would hand
every song in the library a new ID — and playlists, `recentlyPlayed`, room queues and cache keys
all hold those IDs. The self-check already asserts this invariant for the admin edit path ("the ID
survives an edit — playlists and cache keys hold it"); the backfill has to keep it too.

So the backfill is its own small method, and it touches one field:

```csharp
/// <summary>Fills the album on entries indexed before the tag was read. Never touches the ID:
/// playlists and cache keys hold it.</summary>
private static async Task BackfillAlbum(MusicInfo entry)
{
    var path = StorageDirectory + "/" + entry.RelativeLocation;
    if (!File.Exists(path)) return;

    entry.Album ??= (await MediaInfo.GetInformation(path)).Album;
}
```

`??=` rather than `=`: an album an admin typed in Oko outranks a re-read of the file.

### Running it once, not every launch

A gate on `Album is null` would re-probe every genuinely album-less file on every startup. The
entry records which pass wrote it instead:

```csharp
// MusicInfo
/// <summary>Which tag-reading pass produced this entry. Entries below
/// <see cref="MusicManager.ScanVersion"/> are re-read once, then stamped.</summary>
public int Scan { get; set; }
```

In `ParseArtistFolder`, beside the existing legacy loop:

```csharp
var behind = existing.Where(entry => entry.Scan < ScanVersion).ToList();
foreach (var entry in behind)
{
    await BackfillAlbum(entry);
    entry.Scan = ScanVersion;
}
```

and `ParseFile` stamps `entry.Scan = ScanVersion` on everything it creates. `behind.Count > 0`
joins the existing `stale == 0 && newFiles.Count == 0 && legacy.Count == 0` early return so the
folder is written back exactly when something changed. One ffprobe per file, once per library,
parallel per folder as the loader already is — then never again.

Bumping `ScanVersion` is how any future tag becomes a backfill, which is the only reason it is an
int rather than a bool.

### Proof

- `Gaida.Tests/MusicInfoFormatTests.cs`: `Scan` round-trips through `Info.json`, and an entry
  written without it deserializes as `0`.
- `Gaida.Pods.MusicDatabase --self-check`: extend the throwaway-library check with an entry that
  has no `Scan` and no `Album`, and assert that after `InitializeAsync` its **ID is unchanged** and
  the file now carries `"Scan": 1`. That is the invariant worth a test; the tag read itself needs
  ffprobe and a real file, which the self-check has no business shipping.
- Live: Oko's library stats already report `withoutAlbum`. It should fall to roughly the number of
  untagged files, and `GET /Audio/Query?...` should carry `"album"` on the results.

---

## 3. The album, from the API to the screen (`Frontend/`)

`SearchResult.album` already exists and already arrives. Exactly one place drops it:

```ts
// state/current.svelte.ts
album: string = $state('');
// in set(): this.album = now.album ?? '';
// in clear(): this.album = '';
```

And since it is now known, it belongs in the OS media record too — the lock screen has a slot for
it and shows it empty today:

```ts
// TrackInfo.svelte
navigator.mediaSession.metadata = new MediaMetadata({
    title: current.name,
    artist: current.artist,
    album: current.album,
    artwork: [{ src: thumbnail }]
});
```

`TrackInfo` gains the third line, always rendered when the tag exists and hidden by CSS in the bar
shape (where there is no room for it):

```svelte
{#if current.album}
    <span id="track-album" class="eyebrow truncate">{current.album}</span>
{/if}
```

---

## 4. Opening and closing it (`Player.svelte`)

State, three lines, beside `dock`:

```ts
let full = $state(false);
closeOnBack(() => full, () => (full = false));
```

`closeOnBack` is the app's one back handler and already covers Escape, the back button and the
back gesture — the full player is a layer like the dock sheet, and it closes before back leaves
the page.

The attribute drives everything else:

```svelte
<div id="player" data-shape={full ? 'full' : 'bar'} data-hold={holdState} ...>
```

One new button, in `#player-docks`, so it inherits the row's sizing and focus treatment:

```svelte
<button
    type="button"
    aria-label={full ? 'Close the full player' : 'Open the full player'}
    aria-expanded={full}
    onclick={() => (full = !full)}
>
    <Icon src={full ? ChevronDown : ChevronUp} mini size="16" />
</button>
```

In the `full` shape CSS lifts that one button out of the action row and pins it to the top-right
corner; the rest of `#player-docks` stays where the grid puts it.

Two rules of behaviour:

- **Opening a dock collapses the full player.** `toggle()` sets `full = false`. The sheet lives in
  the layout, below the player's stacking context, and a queue you cannot see is worse than a
  cover you briefly cannot. One line, no z-index negotiation.
- **No track, no full player.** The expand button renders only when `current.name` is set — the
  bar is already empty then.
- **Micro wins.** `app.css` scopes micro on `max-height: 320px` and its rules are id-anchored and
  outside `@layer`; the `full` block is written the same way and placed *above* it, so a 300px-tall
  window keeps the micro layout whatever this attribute says.

---

## 5. The two shapes (`app.css`)

New block directly above the micro block, same house style: plain CSS, id-anchored, outside
`@layer`, commented with what each rule is for.

```css
/* ── Full: the cover is the thing you are looking at ─────────────────────────
   A third shape of the same element. Nothing remounts — the audio element and
   its graph live in here, and a component swap would stop the music. */
#player[data-shape='full'] {
    position: fixed;
    inset: 0;
    z-index: 40;
    margin: 0;
    border: 0;
    border-radius: 0;
    background: var(--color-dark-0);
    display: grid;
    align-content: center;
    justify-items: center;
    gap: 1.25rem;
    padding: 1.5rem;
    --cover: min(78vw, 44vh);
    grid-template-areas: 'cover' 'meta' 'actions' 'seek' 'transport';
}

/* the two row wrappers dissolve, exactly as they do at `sm` and in micro, so
   `grid-area` can deal the children into the layout */
#player[data-shape='full'] .player-row,
#player[data-shape='full'] #track-info {
    display: contents;
}

#player[data-shape='full'] #track-info img {
    grid-area: cover;
    width: var(--cover);
    height: var(--cover);
    border-radius: var(--radius-panel);
    object-fit: cover;
}
```

…and `#track-info > div` → `meta`, `#player-docks` → `actions`, `#seekbar` → `seek`, `#controls` →
`transport`, with `#player-cover { display: block }` reusing the micro ground verbatim.

The desktop half is one media query inside that block:

```css
@media (min-width: 640px) {
    #player[data-shape='full'] {
        --cover: min(46vh, 40vw);
        justify-items: start;
        column-gap: 3rem;
        grid-template-columns: auto minmax(0, 26rem);
        grid-template-areas:
            'cover meta'
            'cover actions'
            'cover seek'
            'cover transport';
    }
    #player[data-shape='full'] #track-info img { align-self: center; }
}
```

The signature, as its own rule — the gauge leaves the seek bar and becomes the screen's edge:

```css
#player[data-shape='full'] #seekbar [role='img'] {
    position: fixed;
    inset: 0 auto 0 0;
    width: 2px;
    height: 100vh;
    border: 0;
    border-radius: 0;
}
```

The fill inside it is already `absolute inset-x-0 bottom-0` with its height bound to the runway, and
`.rain-streak` is already `inset-inline: 0; height: 100%` — so the column fills and rains with no
new markup and no new keyframes. `opacity-0` when not buffering is likewise already there.

---

## 6. Room for the lyrics (mechanism only)

Lyrics are not in this plan. The room for them is, because retrofitting a size change onto a fixed
cover is the expensive version of this:

```css
#player[data-shape='full'] #track-info img {
    transition: width 320ms cubic-bezier(0.2, 0.7, 0.3, 1),
                height 320ms cubic-bezier(0.2, 0.7, 0.3, 1);
}
#player[data-shape='full'][data-lyrics='on'] { --cover: min(38vw, 20vh); }

@media (prefers-reduced-motion: reduce) {
    #player[data-shape='full'] #track-info img { transition: none; }
}
```

Nothing sets `data-lyrics` yet. The cover is sized from one custom property and animates when that
property changes, so the lyrics feature adds an attribute and a `grid-area: lyrics` row — not a
second layout.

---

## 7. Phases

| # | Change | Proof |
| --- | --- | --- |
| 1 | `MediaInfo` reads `ALBUM`; `MusicInfo.Scan`; `BackfillAlbum` in `ParseArtistFolder`; `ParseFile` stamps | `dotnet test`; `--self-check` asserts the ID survives the backfill and `Scan` is written; Oko's `withoutAlbum` falls on a real library |
| 2 | `current.album`, `MediaMetadata.album`, `#track-album` in `TrackInfo` | `npm test`, `npm run lint`; the album shows in `SearchRow` and on the lock screen |
| 3 | `full` state, `data-shape`, the expand button, `closeOnBack`, dock collapses it | back / Escape / gesture each close it once; the queue sheet still opens |
| 4 | The `full` CSS block — mobile column | at 320px, 390px and 430px; music keeps playing across every open and close |
| 5 | The desktop media query and the edge gauge | at 1024px and 1920px; the gauge fills while a track buffers |
| 6 | The `--cover` transition and the `data-lyrics` hook | toggling the attribute in devtools shrinks the cover; reduced motion cuts the transition |

Phases 1 and 2 stand alone and are worth shipping first: they fix data that is missing everywhere
in the app, not only in the player this plan draws.

## 8. What the build turned up

Two things the plan did not see, both found in the browser rather than in the code:

- **`transform: none` does not cancel Tailwind v4's centring.** The bar is centred with
  `sm:-translate-x-1/2`, and v4 emits that on the `translate` property, not `transform`. The full
  shape's `transform: none` therefore left the whole fixed layer shifted half a screen to the left
  on desktop. It needs `translate: none`. The micro block has always carried the same bug — it is
  invisible in a 320-wide Discord frame, where `sm:` never matches, and plainly visible in a
  760×480 popped-out window, which is exactly what that block is for. One line fixes both.
- **`blur(2px)` is a micro-sized number.** Over a 300px frame it reads as depth; over a full screen
  it reads as a second, sharp copy of the artwork competing with the real one. The full shape's
  ground blurs by 24px, with `scale: 1.1` on the image so the blur has something to sample at the
  edges.

One thing outside the plan was fixed on the way: `CoverExtractor.ParseFolder` handed every entry's
path to `Id3V2.GetImageFromTag`, which throws on a missing file where Flac and WavPack answer
`null` — so one deleted track took the whole library load down. It skips a path that is not there.

## 9. Deliberately skipped

- **Remaining time (`-4:14`).** The screenshot's left-hand figure counts down. Every other clock in
  this app counts up, and `SeekBar` is shared by all three shapes — a countdown here means a
  countdown in the bar, or a shape-dependent clock. Elapsed and total, unchanged.
- **Drag-to-dismiss.** The back gesture already closes it on Android, and Escape and the button
  cover everything else. Add a pointer handler if the swipe is actually missed.
- **A `/player` route.** The full player is a layer, not a page: a route would unmount `Player` on
  navigation and take the audio element with it.
- **Cover flow / swipe between tracks.** A second track's artwork means a second `<img>` and a
  gesture that competes with the seek bar. Not for the first version.
