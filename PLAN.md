# Implement the "Musicrain Design Direction"

## Context

The attached artifact ("Musicrain Design Direction") audits the in-progress SvelteKit
rewrite (`src/routes`, `src/components/{header,home,player,queue}`,
`src/state/*.svelte.ts`) against the live API and the old React build it's replacing.
It found: five real state bugs in the queue, missing verbs (no "play now"), an
identical home/search layout that answers two different questions badly, a
token/type system that already exists but is too shallow, and several API
endpoints the UI never calls. It ends with its own 7-step build order, ranked by
what unblocks the most downstream work. This plan grounds that order against the
actual files, confirming every bug and gap it names.

**Toolchain, fixed:** the SvelteKit side had no wired build entry — `vite.config.ts`
and `package.json` were still React-only even though `@sveltejs/kit`, `svelte`, and
`tailwindcss` were already sitting in `node_modules` (npm-reported "extraneous").
Registered them in `package.json`/`package-lock.json`, switched `vite.config.ts` to
`sveltekit()` + `@tailwindcss/vite()` (pinning `@sveltejs/vite-plugin-svelte` to
`^4` to match the installed vite@5.4.8), and pointed `tsconfig.json` at
`./.svelte-kit/tsconfig.json` so the `$components`/`$states`/`$requests`/`$lib`
aliases type-check. Verified `npm run dev` serves `/` and `/search` at 200.

## Steps

### 1. Fix the queue state machine, add "play now" — ✅ Done
`src/state/queue.svelte.ts` — all confirmed by reading the file:
- `previousTrack()` did `this.currentIndex += 1` (line 62) — fixed to `-= 1`.
- `removeItem()` guarded with `if (!index) return` (line 28) — falsy-zero bug,
  refused to remove index 0 and let `indexOf`'s `-1` (not found) through. Fixed to
  `if (index === -1) return`.
- `removeIndex()` never re-pointed `current` when you deleted the playing track —
  now calls `setCurrent()` when the removed index was current, and clamps to the
  new last item (or does nothing) at the edges.
- `nextTrack()` at the end of the queue set `audio.currentSeconds =
  current.lengthSeconds` (line 68), which re-fired the `<audio>` `onended` in
  `Audio.svelte`, which called `nextTrack()` again — an infinite loop. Now sets
  `audio.paused = true` instead of nudging `currentSeconds`.
- Dropped the `console.log(this)` in `setCurrent()` (line 78).
- Added a `playNow(item)` verb (insert-and-jump) — today the only way to start
  playback was `add()`, and it only auto-played when the queue was empty. This is
  the method Steps 3 and 5's "Play" buttons will call.

Also fixed `src/state/current.svelte.ts:17` — fallback thumbnail was
`'/static/empty.png'`, but SvelteKit serves `static/` at the root, so it 404'd.
Changed to `/empty.png`, and added an `onerror` handler on the `<img>` in
`Song.svelte`, since `??` never caught a non-null broken URL. (Search rows don't
exist yet — same handler to be added there in Step 3.)

Added `src/state/queue.svelte.test.ts` (8 assertions, one per fixed bug) —
`npx vitest run` passes; `svelte-check` is clean on all touched files.

### 2. Extend the design tokens, load the three type faces — ✅ Done
`src/app.css` already has an `@theme` block (`--color-dark-0`, `--color-surface-*`,
`--color-primary-*`) — deepen the existing values rather than renaming classes,
so `Header.svelte`, `Song.svelte`, `Queue.svelte`, `Player.svelte` etc. don't all
need touching:
- `--color-dark-0` → `#06060d` (was `#080811`), `--color-surface-0` → deepen
  toward `#14121f`/`#1b1828`.
- Add `--color-gold: #e8b04b` (marks library-vs-YouTube source — new, Step 3
  needs it) and a hairline token (`--color-haze: #2a2638`) for dividers.
- Fix the AA-contrast defect: white on `--color-primary-0` (`#8171fc`) is
  ≈3.7:1. Either add `--color-primary-600: #6b57f5` for button fills or set
  button text to the ink token — the design doc's swatch uses the darker fill.
- Add `--font-display: "Unbounded", ...`, `--font-body: "Golos Text", ...`,
  `--font-mono: "JetBrains Mono", ...` and apply body/mono via Tailwind
  utility classes (`font-display`, `font-mono`) generated from those tokens.
  Load the fonts in `src/app.html` (`<link rel="preconnect">` + the Google
  Fonts stylesheet, both Latin+Cyrillic — Cyrillic titles like `Бизнесмен` are
  real library data, not optional).
- `font-variant-numeric: tabular-nums` on the mono/duration classes used in
  Steps 3–4.

### 3. Rebuild `/search` as grouped, source-labelled rows — ✅ Done
- `src/state/search.svelte.ts` — `SearchResult` type is missing `album` (API
  sends it for library results; currently discarded). Add `album?: string`.
- New `src/components/search/SearchRow.svelte`: art · title (clamp 2 lines,
  never truncate to 1) · artist·album (or channel, for YouTube) · duration
  (mono, tabular, right-aligned; a `long` pill over 15:00) · hover/focus-reveal
  actions (Play → `queue.playNow`, Queue → `queue.add`, and an overflow with
  Play next / Download raw / Copy id / Go to artist). Whole row click = play.
- `src/routes/(app)/search/+page.svelte`: partition `results` by
  `id.startsWith('audio://')` vs the rest into two `<SearchRow>` groups —
  "In the library · N" (gold rule) above "From YouTube · N" (neutral rule).
  Heading becomes `"{results.length} results for "{term}""`; empty state gets
  real copy ("Nothing matched **{term}**. Try an artist name, or paste a
  YouTube link.") instead of a blank list.
- `src/components/header/Header.svelte`: the search `<input>` never
  repopulates from `?term` — bind its value from the current URL (via
  `page.url.searchParams.get('term')`, `$app/state`'s `page`). While in this
  file, also fix the avatar: it's a plain `<div>` with `cursor-pointer` — not
  focusable or keyboard-activatable. Make it a `<button>`.

### 4. Build the queue dock against the room-verb table — ✅ Done
`src/components/queue/Queue.svelte` — restructure from the current absolutely-
positioned `h-128` overlay to a 380px right dock that narrows page content
(touches `src/routes/(app)/+layout.svelte`, which currently just stacks
`Header`/`main`/`Player`). Three zones: **Now playing** (56px art + progress
hairline), **Next up · N** (numbered — numbering is the one place position is
real information), **Played** (collapsed by default). Footer: track count +
remaining time (mono) + Shuffle + Clear.

Build every gesture against the local queue methods that already exist or that
Step 1 added, since this is the exact table the future Discord-room queue
reuses verbatim:

| Gesture | Local call |
|---|---|
| Drag row above next track | `queue.setNext(i)` *(already exists)* |
| Remove ✕ | `queue.removeIndex(i)` *(already exists, fixed in Step 1)* |
| Double-click a row | `queue.playNow`-equivalent / set current directly |
| Shuffle | new `queue.shuffle()` |
| Add from search | `queue.add(item)` *(exists)* |

Reordering: native `draggable` + `ondragover` (no library — the doc calls this
"roughly thirty lines"). Also fix the noted bug while in this file: the
`{#each items as item, index (index)}` keys on `index`, so a reorder re-renders
every row and drops the drag — key on `item.id` instead.

## Current handover status

Steps 1–4 are complete in the current worktree. The React implementation and
its generated styles have been removed; `master` now fast-forwards to the
Svelte branch history. `npm test`, `npm run type-check`, `npm run lint`,
`svelte-check`, and the production build pass.

Step 5 is next. Before its paste-a-link flow can be connected, the backend
agent must restore and document `FindQueryType`; it returned HTTP 502 during
frontend verification. The new [backend handover](./BACKEND_HANDOVER.md)
records the current result schema, existing frontend calls, required artist
endpoints, and acceptance criteria.

### 5. Rebuild `/` around "the roll" — ✅ Done
`src/routes/(app)/+page.svelte` / `+page.ts`:
- Hero: one random library track at full size (reuse `getRandomSongs`, or a
  count=1 call), "Roll again" re-rolls client-side without navigation, "Play"
  calls `queue.playNow`.
- "More from this artist" rail: new request fn hitting
  `GET /Audio/Artist/Local?term=` (unused today) with the hero's artist.
- "Back where you left off": last 12 played, read/write in `localStorage` only
  (no server, no account) — small helper, e.g. `src/lib/recentlyPlayed.ts`.
- "Artists in the library": one `RandomResults?count=200`, grouped client-side
  by `artist`, rendered as plain text chips (no art — most library entries
  share one cover per artist, so art here is noise per the doc).
- Paste-a-link input resolves through `FindQueryType`: text opens search, a
  local/YouTube video starts immediately, and a playlist starts its first item
  then queues the remaining results. JSON resolver errors are shown inline.
- Keep the "(Curated) Picks" label as-is; rename the "Get More" button to
  "Roll again" only.

### 6. Fill the artist page — ✅ Done
`src/routes/(app)/artist/+page.svelte` is currently an empty stub. Add
`src/routes/(app)/artist/+page.ts` loading `GET /Audio/Artist/Local?term=` and
`GET /Audio/Artist/YouTube?term=` from a `?term` query param, rendered as two
tabs reusing `<SearchRow>` from Step 3. This is what the home hero (Step 5) and
every search row's "Go to artist" action link to.

### 7. Player bar — six tweaks, no restructure — ✅ Done
All in `src/components/player/` (layout/proportions unchanged):
- `src/lib/time.ts`: `getTimeString` always returns `HH:MM:SS` (uses
  `toISOString`, wraps silently past 24h) — drop the hours segment under an
  hour.
- New small format-readout (mono tile, e.g. `OPUS · 112`) next to `Volume.svelte`
  reading `quality.codec`/`quality.bitrate` (state already exists, nothing
  renders it) — opens the existing bitrate picker on click (the FLAC-hides-
  bitrate behavior is already implemented per the recent "Disable selecting
  the bitrate for FLAC" commit).
- Buffering tint: in `Audio.svelte`/`SeekBar.svelte`, when `audio.bufferedSeconds`
  is within ~3s of `audio.currentSeconds`, swap in the "rain gauge" buffering
  indicator (a filling column — pure CSS, no new dependency).
- Queue toggle: give the `QueueList` icon/checkbox in `Player.svelte` a count
  badge (`queue.items.length`) and a visible checked/active state.
- Hide the `ChatBubbleOvalLeft` icon in `Player.svelte` until multiplayer
  lands — it's currently a dead control.
- Mobile overflow: `#player`'s `max-w-7xl` + `-translate-x-1/2` runs off the
  viewport under ~500px — clamp width, let the seek bar take the row it needs.

## Completion update

The backend's documented `FindQueryType` response is now integrated. A live
request for an ordinary query returned `{"kind":"search","query":"hello"}`.
The home page uses `RandomResults` with counts 1, 30, and 200 for its hero,
curated picks, and artist chips; the artist rail and artist page use the two
new artist endpoints. Recently played entries are stored locally (maximum 12).

The player now renders a format picker, hides bitrate choices for FLAC, marks
low-buffer playback with a CSS rain gauge, hides the unfinished chat control,
and keeps its width inside a narrow viewport.

## Verification

- After Step 1: manually add 3+ tracks to the queue and confirm Previous goes
  backward, removing index 0 works, deleting the currently-playing track
  re-points playback, and the queue doesn't loop/replay the last track forever.
  *(Automated: `src/state/queue.svelte.test.ts`, `npx vitest run`.)*
- After Step 2: check `Header`, `Song`, `Player` render against the new tokens
  in a browser; run an automated contrast check (or eyeball) on button text
  against the (possibly darkened) primary fill.
- After Step 3: `/search?term=daft%20punk` (or any term with library hits)
  shows two labelled groups, the input keeps the term on reload, and an
  unmatched term shows the empty-state copy.
- After Step 4: drag-reorder, remove, and double-click-to-play all work against
  a multi-item queue without losing selection; resizing to a narrow viewport
  doesn't clip the dock.
- After Step 5: `/` shows a hero, an artist rail, chips, and recently-played
  (once something's been played) without console errors.
- After Step 6: `/artist?term=<artist>` returns both tabs' data.
- After Step 7: play a track under a minute (time format), toggle FLAC vs Opus
  (format readout / bitrate hiding), and resize below 500px (no overflow).
- Throughout: `npm run test` / `tsc --noEmit` (toolchain now wired — both run
  against the real SvelteKit + Tailwind build).
