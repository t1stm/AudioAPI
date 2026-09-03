# Gapless playback outside a room

**Status: steps 1–4 built and measured. Step 5 not needed — see "What the
measurement said".**

Companion to [`PLAN.md`](./PLAN.md) and [`MULTIPLAYER_PLAN.md`](./MULTIPLAYER_PLAN.md).
Scope is solo playback only: in a room the server owns the advance (`end` →
finishing barrier → `current`), and nothing here changes that. Everything below
still helps a room client — it makes the `loaded` answer arrive sooner — but the
moment of switching tracks stays the server's to decide.

## What the gap is made of today

The player is one `<audio>` element whose `src` is `current.url`, and a track
change is a `src` change:

`onended` → `queue.nextTrack()` → `setCurrent()` → `current.set()` rebuilds the
`/Download/{codec}/{bitrate}` URL → the element runs the resource-load algorithm
against the network → `canplay` → `apply()` plays it.

`queue.preloadNext()` (`src/state/queue.svelte.ts:142`, fired at T−20 s by the
sampling effect in `Audio.svelte:113`) removes one term from that sum: the encode
is already warm, so the Download that follows joins a finished or in-flight
encode instead of starting ffmpeg cold. Everything else is still paid at the
switch:

1. a fresh HTTP request — connection reuse if lucky, TLS and TTFB if not,
2. enough bytes transferred for the element to reach `canplay`,
3. the element's own load/decode/start latency,
4. codec priming at the file boundary.

Items 1 and 2 are the audible part, and they are the ones a prefetched **body**
removes. That is the change this plan is about: do the Download call while the
current track is still playing, hold the bytes, and hand them to the element as a
local resource when the track ends.

Item 4 is not solvable with an `<audio>` element at all — see "What this does not
buy" at the end.

## Step 1. Prefetch the body, not just the encode

New in `src/requests/songs.ts`, next to `preloadSong`, which it supersedes for the
next-track case (keep `preloadSong` for the hover warm-up in `Controls.svelte:28`
— hovering Next should not pull down a few megabytes).

```ts
type Prefetch = { key: string; url: string; abort: AbortController }
let held: Prefetch | null = null
```

- `prefetchSong(id: string)` — builds the same path `current.set` builds, keyed as
  `` `${codec}/${bitrate}/${id}` `` so a quality change invalidates it. If `held`
  already has that key, return. Otherwise abort and revoke whatever is held, then
  `fetch(url, { signal })` → `response.blob()` → `URL.createObjectURL(blob)`.
- `takePrefetched(id: string): string | null` — returns the object URL for a
  matching key and hands ownership to the caller, clearing `held` without revoking.
- `dropPrefetch()` — abort in flight, revoke, clear. For quality changes and for
  unmount.

Failure is silence: a rejected fetch clears `held` and the track loads over the
network exactly as it does today. Never let a prefetch error reach the UI.

One entry, not a cache. Two tracks of held audio is 8–12 MB at Opus 112, and the
only entry anyone ever asks for is the next one.

## Step 2. Spend it in `current.set`

`src/state/current.svelte.ts` is the single place a playable URL is built, so it
is the single place the prefetched one is substituted:

```ts
this.url = takePrefetched(now.id) ?? `${audioApi}/Download/...`
```

Ownership then follows the track. `Current` keeps the previous object URL in a
private field and revokes it on the next `set()`/`clear()` — one blob alive at a
time, released as soon as the element has moved on. Revoking on the _next_ set,
not the current one, is what stops a revoked URL being handed to an element that
has not finished loading it.

Because it lands in `current.set`, the room path gets it too: a room `current`
frame that names the track we prefetched loads from memory and answers the
loading barrier immediately.

## Step 3. Trigger it earlier than 20 s

Built without the `Queue.prefetchNext` wrapper this section first proposed: the
effect below already has the id in hand, so it calls `prefetchSong` directly and
there is nothing for a method to add. `Queue.preloadNext` stays as it was, for
the hover warm-up.

The trigger is `audio.bufferedSeconds` in `Audio.svelte` reaching
`current.lengthSeconds`, which is what `oncanplaythrough` pins it to — the moment the current track owes the network
nothing, so the download competes with no playback and a slow link gets the whole
remaining track duration rather than 20 seconds. Reading it as state rather than
hanging off the event is what makes the same effect handle invalidation too. The
existing T−20 s `queue.preloadNext()` stays as the fallback for when
`canplaythrough` never fires.

Invalidate on the two things that make the held blob wrong:

- the item at `currentIndex + 1` changed (`add`, `remove`, `setnext`, `shuffle`,
  `skipto`, a room `queue` frame) — the key check makes the unchanged case a no-op
  and a real change aborts the old fetch,
- `quality.codec`/`quality.bitrate` changed — same thing: a different key, so the
  held bytes are dropped and the right ones requested.

One `$effect` in `Audio.svelte` reading `queue.items[queue.currentIndex + 1]?.id`,
`quality.codec` and `quality.bitrate` covers both without touching every queue
verb: `prefetchSong` drops the old entry itself when the key changes, and a no
longer existing next track calls `dropPrefetch`.

## Step 4. Verify

`src/requests/songs.test.ts` (new, vitest, mocked `fetch`, `URL.createObjectURL`
and `URL.revokeObjectURL`) covers the six ways the single held entry can be
wrong: handed over once and only once, a miss at a quality it was not encoded at,
no second request for the track already held, abort-and-revoke when the next
track changes, abort when the track now starting is still downloading, and
nothing held after a failed download.

### What the measurement said

Two numbers, because the whole plan is a latency claim.

The network term, measured with `curl` against the deployed API on a wired link —
this is what the prefetch moves off the switch:

|                                               | TTFB     | total          |
| --------------------------------------------- | -------- | -------------- |
| cold encode                                   | 56–79 ms | **390–620 ms** |
| warm encode (what `preloadSong` already buys) | 36–43 ms | **52–71 ms**   |

The switch itself, in headless Chromium with the HTTP cache disabled, timed from
`ended` to `playing` over three track changes, with the same tracks played each
way:

|                                        | run 1    | run 2     | run 3     |
| -------------------------------------- | -------- | --------- | --------- |
| today: `src` set to the Download URL   | 36 ms    | 40 ms     | 84 ms     |
| prefetched: `src` set to the held blob | **7 ms** | **10 ms** | **12 ms** |

Both control numbers are flattering to the current code: the API is one hop away
here and answers in tens of milliseconds. A phone on mobile data pays the cold
column, and the prefetched column does not move — that is the point of it.

Also learned on the way: `/Download` already answers `cache-control: public,
max-age=31536000, immutable` and `accept-ranges: bytes`, so a bare
`fetch()`-and-discard would warm the HTTP cache and probably serve the element
from disk too. The blob is kept because it does not depend on media-cache
behaviour that differs per browser, and because the numbers above say it works.
(A cache-busting query parameter drops `accept-ranges` from the response, which
costs Chromium the duration of an Ogg file entirely — worth knowing before anyone
adds one.)

## Step 5, not needed. A/B elements

**Not built.** Ten milliseconds is under the threshold of hearing a seam, and it
is smaller than the codec padding at the file boundary that no element-level work
removes. Revisit only if a real device — a phone, a Bluetooth output — measures
the switch materially worse than headless Chromium did.

The remaining delay is the element's resource-load algorithm running at the moment
of the switch. Removing it means having a second element already at
`HAVE_ENOUGH_DATA` when the first one ends.

`Audio.svelte` renders two `<audio>` elements and keeps `active: 0 | 1`. The
standby element gets the next track's URL (the prefetched blob, so it costs no
second download) as soon as `current` settles. `onended` on the active element
flips `active` and calls `play()` on the standby, which starts from a buffered
resource in single-digit milliseconds. The old active becomes the new standby and
takes the following track.

What that costs, and why it is not Step 1:

- Every handler in the file — `anchor`, `position`, the seek effect, the retry
  path, the buffered gauge — becomes "the active element" instead of "the
  element". That is the whole synchronisation surface, including the latency
  accounting the room depends on.
- `createMediaElementSource` is once per element for the life of the page, so both
  source nodes are built once at mount and both connect to `context.destination`.
  That part is fine; it is the state indirection that is the work.
- iOS Safari will not `play()` an element that has never been started from a user
  gesture. The standby element has not been. Solo autoplay through a chain of
  tracks would need both elements unlocked by the first gesture — the gate in
  `audio.unblock` is the place to do it, and it needs testing on a real device
  before this step is called done.

Do this only with a measured number, from a real device, saying the blob swap
left an audible gap.

## What this does not buy

Sample-accurate gapless. Even with zero switching latency, an encoded file has
priming and padding at its edges, and two separately encoded files played back to
back have silence between them that no HTML media element removes. Getting rid of
that means either MSE (append both tracks into one `SourceBuffer` on one timeline,
which constrains the codec set to what MSE accepts and rules out raw Ogg) or Web
Audio (`decodeAudioData` into an `AudioBufferSourceNode` scheduled at an exact
`context.currentTime`, which means rebuilding the position clock, the rate
steering, seeking and the buffered gauge on top of buffer sources).

Both are real projects, and the switch now measures at a few milliseconds without
either. Album-continuous mixes still have a seam; ordinary tracks do not sound
like they have one.
