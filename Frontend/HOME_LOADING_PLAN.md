# Plan: skeleton placeholders on the home page, filled by a streamed response

## The problem, measured against the code

`src/routes/(app)/+page.ts` blocks the route on four API calls, in two serial waves:

```ts
const hero = (await getRandomSongs(fetch, 1))[0] ?? null;   // wave 1 — blocks everything
const [songs, librarySongs, artistSongs] = await Promise.all([
    getRandomSongs(fetch),                                   // wave 2
    getRandomSongs(fetch, 200),                              // wave 2 — heaviest payload
    hero ? getArtistLocal(hero.artist, fetch) : []           // wave 2
]);
```

The app is a pure SPA (`adapter-static` with `fallback: 'index.html'`, nothing prerendered), so this
runs in the browser on every visit. SvelteKit does not render the route until `load` resolves, which
means the user stares at nothing for the length of two round trips to `api.gergov.bg` — and the
slowest member of the second wave is a 200-track sample fetched only to count artist names.

Nothing on the page needs to arrive together, and nothing needs to arrive *whole*. A row of thirty
picks can start filling from the first track the API produces.

## The shape of the fix

1. Stop awaiting in `load`, so the route renders on the first frame.
2. Draw a placeholder for every slot the section is going to hold.
3. Read each response as a stream and drop each track into its own slot as it arrives; slots that
   have not been filled yet keep their placeholder, and slots the API never fills are dropped when
   the stream ends.

Point 3 is the whole reason the client parser matters: with `await response.json()` the browser has
nothing until the last byte, so a streaming server buys nothing.

## Phase 1 — an incremental JSON reader

New file `src/lib/streamJson.ts` (pure utility, same home as `time.ts` and `playbackClock.ts`):

```ts
/**
 * Yields each top-level JSON object out of a response body as its closing brace
 * arrives. Everything outside a string that is not part of an object — the array's
 * own brackets, commas, whitespace, newlines — is skipped, so this reads both what
 * ASP.NET Core writes for an `IAsyncEnumerable<T>` (`[{…},{…},…]`, flushed as it
 * goes) and newline-delimited JSON, with no content-type branch.
 * ponytail: objects only. Every streaming endpoint here returns a list of records;
 * a general JSON-value parser would be five times the code for no caller.
 */
export async function* streamJson<T>(body: ReadableStream<Uint8Array>): AsyncGenerator<T> {
    const reader = body.pipeThrough(new TextDecoderStream()).getReader();
    let buffer = '';
    let cursor = 0;
    let start = -1;
    let depth = 0;
    let inString = false;
    let escaped = false;

    for (;;) {
        const { value, done } = await reader.read();
        if (value) buffer += value;

        while (cursor < buffer.length) {
            const char = buffer[cursor++];
            if (inString) {
                if (escaped) escaped = false;
                else if (char === '\\') escaped = true;
                else if (char === '"') inString = false;
            } else if (char === '"') inString = true;
            else if (char === '{') {
                if (depth++ === 0) start = cursor - 1;
            } else if (char === '}' && --depth === 0) {
                yield JSON.parse(buffer.slice(start, cursor)) as T;
                buffer = buffer.slice(cursor);
                cursor = 0;
                start = -1;
            }
        }

        if (done) return;
    }
}
```

`TextDecoderStream` handles multi-byte characters split across chunk boundaries; the scanner state
(`depth`, `inString`, `escaped`) persists across reads, so an object split mid-string is fine. The
cursor only ever moves forward, and the buffer is trimmed at each object, so a 200-track response
does not turn into quadratic string work.

**This ships against today's non-streaming endpoints.** If the API sends the whole array at once, the
last `read()` delivers everything and the generator yields all of it in one pass — identical result,
no waiting on backend work.

### Its test

`src/lib/streamJson.test.ts` (vitest is already set up; `src/requests/songs.test.ts` is the pattern).
One test, feeding a hand-built `ReadableStream` chunked at deliberately awkward points — mid-object,
mid-string, mid-escape sequence, and with the array's `[`/`,`/`]` landing alone in a chunk — and
asserting both the parsed objects and that they arrive one at a time rather than all at the end. A
character scanner with string-and-escape state is exactly the kind of thing that needs one runnable
check.

## Phase 2 — a streaming request in `src/requests/songs.ts`

Alongside the existing `getResults`, which stays for every caller that wants an array:

```ts
async function* streamResults(fetcher: Fetcher, path: string): AsyncGenerator<SearchResult> {
    const response = await fetcher(`${audioApi}${path}`);
    if (!response.ok || !response.body) {
        const payload = await response.json().catch(() => null);
        throw new AudioApiError(payload?.error?.message ?? `The audio service returned ${response.status}.`, response.status);
    }
    for await (const result of streamJson<SearchResult>(response.body)) {
        // one at a time, so the thumbnail is already rewritten by the time the row renders
        yield proxyThumbnails([result])[0];
    }
}

export function streamRandomSongs(fetcher: Fetcher, count = 30, youTubeShare?: number) {
    const share = youTubeShare === undefined ? '' : `&youTubeShare=${youTubeShare}`;
    return streamResults(fetcher, `/RandomResults?count=${count}${share}`);
}

export function streamArtistLocal(term: string, fetcher: Fetcher) {
    return streamResults(fetcher, `/Artist/Local?term=${encodeURIComponent(term)}`);
}
```

Same paths, same error type. `getRandomSongs` / `getArtistLocal` are left untouched — `/search`,
`/artist` and the single-track hero roll keep using them.

## Phase 3 — `src/routes/(app)/+page.ts`

Return the started work, await none of it. A `load` that awaits nothing resolves synchronously, so
SvelteKit renders on the first frame, and the requests go out in parallel instead of queueing behind
the hero.

```ts
export const load: PageLoad = ({ fetch }) => {
    const hero = getRandomSongs(fetch, 1)
        .then((songs) => songs[0] ?? null)
        .catch(() => null);

    return {
        hero,
        picks: streamRandomSongs(fetch),
        librarySongs: streamRandomSongs(fetch, 200),
        // needs the hero's name, so it is the one thing that still chains — but it
        // no longer holds up anything else
        artistSongs: hero.then((song) => (song ? streamArtistLocal(song.artist, fetch) : null))
    };
};
```

The hero stays a plain promise: it is one track, so there is nothing to stream. The `.catch()` is not
decoration — with an awaited `load` a failed request lands on SvelteKit's error page, whereas an
unawaited one would surface as an unhandled rejection. Failing soft matches the page's existing "The
roll is resting for a moment" fallback, and means one dead endpoint no longer blanks the page. The
generators are caught at the consumer instead, where the section they belong to can be emptied.

Note that async generators are *lazy*: `streamRandomSongs(fetch)` does not send the request until the
first `next()`. The component starts iterating in its initialisation, in the same tick, so this costs
nothing — but it is the reason the calls cannot simply be left dangling.

## Phase 4 — `src/routes/(app)/+page.svelte` fills slots

The component already keeps writable copies of the loaded data (the roll mutates `hero`,
`artistSongs` and `curated`). They become slot arrays: a fixed number of `null`s, replaced by index.

```ts
let hero = $state<SearchResult | null>(null);
let heroLoading = $state(true);
let curated = $state<(SearchResult | null)[]>(Array(30).fill(null));
// the artist endpoint has no count to ask for, so guess a row's worth and let the
// stream's end settle it
let artistSongs = $state<(SearchResult | null)[]>(Array(6).fill(null));
let artists = $state<[string, number][]>([]);
let artistsLoading = $state(true);

/** Each track lands in its own slot; the leftovers are dropped when the stream ends. */
async function fill(slots: (SearchResult | null)[], stream: AsyncIterable<SearchResult>) {
    let index = 0;
    try {
        for await (const song of stream) slots[index++] = song;
    } finally {
        slots.length = index;
    }
}

data.hero.then((song) => {
    hero = song;
    heroLoading = false;
    if (song) lookUpVariant(song, rollToken);
});
fill(curated, data.picks).catch(() => {});
data.artistSongs.then((stream) => (stream ? fill(artistSongs, stream) : (artistSongs.length = 0))).catch(() => {});
countArtists();
```

Details that matter:

- `slots[index] = …` and `slots.length = …` are both tracked on a `$state` array (Svelte 5 proxies
  it), so each arriving track re-renders exactly its own slot.
- The `{#each}` keys change from `(song.id)` to the index, because the slot, not the track, is what
  persists across the fill. Position is fixed here, so an index key is the correct one.
- `lookUpVariant` moves out of `onMount` into the hero's `.then()` — the hero does not exist at mount.
  `onMount` keeps only `recentlyPlayed = getRecentlyPlayed()`, which is `localStorage` and instant.
- `rollAgain` and `rollPicks` reuse the same path: reset the slots to `null`s, then `fill` again, so a
  re-roll also fills in progressively instead of blanking and popping.
- The 200-track sample is the exception. Its tally is sorted by count, so updating per track would
  reshuffle the tag cloud two hundred times. `countArtists()` drains the stream into a plain local
  array and assigns `artists` once at the end — skeleton pills until then, one settled cloud after.

## Phase 5 — the placeholders

One component, `src/components/Skeleton.svelte`:

```svelte
<script lang="ts">
    let { class: klass = '' }: { class?: string } = $props();
</script>

<div class="animate-pulse rounded-art bg-surface-200 {klass}" aria-hidden="true"></div>
```

And `src/components/home/song/SongSkeleton.svelte` — the Song card's box, used in two rows: a
`size-36 sm:size-48` square plus two short text bars, inside the same `min-w-36 sm:min-w-48` column,
so a filled slot and an empty one are the same size to the pixel.

| Section | Placeholder |
| --- | --- |
| Hero | The same `grid gap-5 p-4 sm:grid-cols-[11rem_1fr] …` wrapper: a square at the art's `max-w-32 sm:max-w-44 lg:max-w-60`, an eyebrow bar, a title bar, an artist bar, a meta bar, two `h-11` button bars, and the odds slider as a flat `h-2` bar. |
| More from this artist | Six `SongSkeleton`s, replaced by index as the stream lands. |
| Artists in the library | Twelve pill skeletons, `h-9`, widths varied between `w-20`/`w-28`/`w-36` so it reads as text rather than a grid. |
| (Curated) Picks | Thirty slots in the existing `grid grid-flow-col-dense grid-rows-2`, each a `SongSkeleton` until its track arrives. |
| Paste a link or ID | Nothing. No data dependency, so it renders live and usable on the first frame — the page is interactive before any response starts. |
| Back where you left off | Nothing. `localStorage`, synchronous. |

Accessibility, not optional:

- Skeletons carry `aria-hidden="true"`; a section still filling carries `aria-busy="true"`.
- One `<p class="sr-only" aria-live="polite">` per loading region, announcing "Loading the roll" and
  clearing when done, so a screen reader hears the page working instead of reading empty boxes. It
  announces on completion, not per track — thirty announcements would be worse than none.
- Tailwind's `animate-pulse` ignores reduced motion. One rule in `src/app.css`, matching the
  reduced-motion blocks already there:

```css
@media (prefers-reduced-motion: reduce) {
    .animate-pulse { animation: none; }
}
```

## What the API has to do for the streaming half to pay off

The frontend is correct either way; these are what turn "renders instantly, fills in one go" into
"fills track by track".

- Return `IAsyncEnumerable<SearchResult>` from `/RandomResults` and `/Artist/Local`. System.Text.Json
  serialises it as a JSON array without materialising the list.
- Flush per item. ASP.NET flushes when its output buffer fills, which for small records means the
  first tracks can sit in a 16 KB buffer until the end. If per-track delivery does not show up in
  DevTools, write NDJSON manually — `await JsonSerializer.SerializeAsync(Response.Body, item)`,
  a `"\n"`, then `await Response.Body.FlushAsync()` per item. The client parser reads that form with
  no change.
- No response compression or output caching on those routes; both buffer the whole body. The response
  must be chunked, with no `Content-Length`.
- Inside the Discord activity the request goes through `/.proxy/api/Audio`, i.e. Discord's proxy,
  which may buffer regardless. Worth measuring; the degraded behaviour is exactly today's.
- If the enumeration throws halfway, the array is already partially written and cannot be turned into
  an error status. The client sees a truncated body: the scanner yields what completed, `fill`'s
  `finally` trims the rest, and the section ends up short rather than broken.

## Out of scope, worth noting

- The 200-track fetch exists solely to tally artist names. After this it no longer blocks paint, but
  it is still the heaviest request on the page; the real fix is an endpoint that returns artist
  counts. Leaving a `ponytail:` comment at the tally rather than doing it now.
- `/browse`, `/search`, `/artist` and `/playlists` all block on their own awaited `load`. The same
  pattern applies to each, and `streamResults` is now there for them; this plan covers the home page.

## Verification

- `npm run type-check`, `npm run lint`, `npm run test` — including the new `streamJson` test.
- Manual pass with DevTools throttled to Slow 3G: the hero frame, the paste box and every skeleton on
  screen before the first response byte, and no section shifting position as it fills.
- Once the API streams, confirm in the network panel that `/RandomResults?count=30` shows a growing
  response body, and watch the picks row fill in slot by slot rather than at once.
