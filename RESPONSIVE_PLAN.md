# Responsive plan — compact and micro

Two targets, one rewrite:

- **compact** — phone-width and the Discord activity on mobile. Everything under
  `sm` (640px), which today is "the desktop layout, stacked" rather than a design.
- **micro** — a 350×200 frame or smaller, player controls only. Discord's
  picture-in-picture, or a popped-out browser window.

`sm` and up is finished and stays as it is. Nothing below is a repaint of the
desktop layout.

## Tokens: reuse, don't reinvent

The palette in `src/app.css` is already specific to this app — near-black
ground, `--color-primary-0` purple, `--color-gold` for a held room,
`--color-ember` for anything off-library, and a rain vocabulary that runs
through the buffer gauge, the session rail, and the cloud mark. A new token
system for a viewport change would be a new identity for no reason. Colours,
radii, and the display/body/mono trio carry over unchanged.

One type decision is new. Unbounded is wide, and at the 15px a 350px frame
affords it stops being a display face and starts being a legibility problem.
**Micro drops Unbounded**: Golos for the track title, JetBrains for the clock.
The display face returns at compact and above.

## The one new breakpoint

```css
/* app.css, after the `dark` variant */
@custom-variant micro (@media (max-height: 320px));
```

Height, not width — that is the whole point. A phone is narrow _and tall_: it
gets compact. A PiP frame is small in both, and 320px of height is the honest
test for "there is no room for a page here, only a player." A 350×200 frame
hits it; a 390×844 phone does not.

No `ACTIVITY_LAYOUT_MODE_UPDATE` subscription. An iframe's own size is what its
media queries measure, so one CSS rule covers Discord PiP, a resized browser
window, and a landscape phone with no SDK code at all.
→ skipped: the layout-mode event. Add it only if a mode needs to differ from
what its size implies.

## Micro: what it looks like

The frame has two edges and a cover. Nothing else.

```
350 × 200
╔══════════════════════════════════════╗ ← room rail, 1px
║ ▓▓▓▓▓▓ cover, cropped, scrimmed ▓▓▓▓ ║   fog / gold held / primary playing
║                                      ║
║           Midnight City              ║   Golos 15/1.15, 2-line clamp
║           M83                        ║   fog 12
║                                      ║
║         ‹‹      ▶      ››            ║   44px targets, 32px glyph on play
║                                      ║
║   1:42                        4:03   ║   JetBrains 10, fog
╚══════════════════════════════════════╝ ← seek rail, 3px drawn / 16px hit
```

**Signature: the frame's own edges do the reporting.** The top hairline is the
room — it is `SessionStrip`'s existing `.rail`, lifted out and reused, so
holding is gold and synced is primary without a word of text. The bottom
hairline is the track: progress, buffer ahead, and when the buffer runs dry it
becomes the `rain-streak` that already exists in `app.css`. One pixel of sync
state, three pixels of transport, and the artwork gets everything in between.

That is the risk and it is the only one: no labels, no chrome, two coloured
lines and a cover. It earns its place because both lines are components this
app already ships — the small mode is the existing rain vocabulary at the only
scale where it has to carry the entire interface alone.

Below ~260px wide: prev and the two timestamps drop. The bottom rail is the
clock.

## Phase 0 — plumbing

`src/app.css`:

- add the `micro` variant above
- delete nothing else

`src/routes/(app)/+layout.svelte`:

- `Header`, `SessionStrip`, `main`, and the dock `<aside>` get `micro:hidden`.
  **Hidden, not unmounted** — `main`'s route component owns the room's
  `session.connect` effect and `rooms.connect` cleanup, so unmounting it drops
  the user out of the room the player is still playing. `display:none` keeps
  every effect alive.

`src/components/player/Player.svelte`:

- `absolute bottom-2 left-2 right-2` → `static` in compact, `sm:absolute` with
  the current float values. Same DOM, same node order — it is already the last
  flex child of the `h-svh` column, so static docks it correctly.

That last change is the point of the phase. Every page currently pads for a
player that floats over it, with the height guessed separately in each file
(`pb-36 sm:pb-28`, `pb-28 sm:pb-32`, `pb-28 sm:pb-32`, `pb-36 sm:pb-28`,
`pb-36 sm:pb-28`). A docked player needs no padding at all, so all five compact
values are deleted and only the `sm:` float value remains — one number instead
of ten, and on a 400px-tall viewport we stop spending 144px on nothing.

## Phase 1 — touch (this is a bug, not a layout)

`src/lib/sliderInteractions.svelte.ts` handles `MouseEvent` only. **The seek bar
and the volume slider do not work on a touchscreen at all** — no drag, no tap
to position. Both `SeekBar.svelte` and `Volume.svelte` construct the same class,
so one file fixes both callers.

- `mouseMove` / `mouseDown` / `mouseUp` → `PointerEvent`, bound as
  `onpointermove` / `onpointerdown` / `onpointerup`. Pointer events cover mouse,
  touch, and pen; the existing `clientX` maths is unchanged.
- `setPointerCapture` on down so a drag that leaves the 8px-tall track keeps
  tracking, instead of `leave` cancelling it the moment a finger wanders.
- `touch-action: none` on both tracks, or the browser scrolls the page instead
  of scrubbing.
- keep `keydown` exactly as is.

Tap targets to the 44px floor: `SearchRow`'s `…` menu (currently a 16px icon in
`p-1` ≈ 24px), the chat/queue/quality buttons in `Player`, `Song`'s `+`. Padding
only — the drawn size stays.

**Check:** `src/lib/sliderInteractions.test.ts`, matching the existing
`*.test.ts` convention. A synthetic pointerdown at 25% of a stubbed rect sets
`percentage` to 25 and fires `onChange`; a move without a preceding down does
not. That is the whole of the logic that can silently break.

## Phase 2 — chrome

**`Header.svelte`** — three flex children fighting for 350px. Fix by deletion:
drop the submit button on compact. The form is already `action="/search"` with
`name="term"`, so Enter submits natively — the button is redundant everywhere
and merely survivable on desktop. Compact keeps the cloud mark (wordmark
`hidden sm:inline`), the input, and the avatar. Height 56 → 48.

**`SessionStrip.svelte`** — the numbers are already `hidden md:block`, so
compact is just title, rename, state word, roster, Leave. Height 46 → 38,
padding `0 1rem` → `0 0.75rem`, roster count folds into the state word
(`holding · 4`). The drop animation stays; it is 48 spans and it is the app's
signature.

**`Player.svelte`** — compact is currently three stacked rows (~110px). Two:

```
┌────────────────────────────────────────┐
│ [art] Title            ‹‹  ▶  ››  ⌄    │  row 1
│       Artist                           │
│ 0:41 ▓▓▓▓▓▓▓░░░░░░░░░░░░░░░░░░░  3:58  │  row 2, full width
└────────────────────────────────────────┘
```

Volume drops on compact — device volume keys own it, and the slider costs 96px
of a 350px row. Quality, chat, and queue collapse behind the one `⌄`.
→ skipped: an in-app volume control on touch. Add it if someone asks.

## Phase 3 — pages

- **`/` home** — hero grid already stacks; give the artwork `max-w-40` on
  compact so the title is above the fold. The `Song` card is a fixed
  `min-w-48` / `size-48`; drop to `min-w-36` / `size-36` under `sm` so two cards
  peek in a 350px rail and the horizontal scroll is discoverable. Paste-a-link
  form is already `flex-col sm:flex-row`. Artist chips are fine.
- **`/search`** — `SearchRow`'s
  `grid-cols-[2.75rem_minmax(0,1fr)_auto_auto]` keeps four columns at every
  width. Compact goes to `[2.75rem_minmax(0,1fr)_auto]`: the duration merges
  into the artist line as `M83 · 4:03`, the `long` badge stays, and the actions
  column becomes the `…` menu alone (Play and Queue are already `sm:`-gated, so
  their absence is existing behaviour — the menu just has to be tappable, per
  phase 1).
- **`/artist`** — the two tabs are `w-fit` and overflow past ~380px because both
  labels carry counts. Compact: `In the library · 12` → `Library 12`, tabs go
  `flex-1` on a full-width strip.
- **`/rooms`** — the `roomID.slice(0,8)` mono column is already `hidden sm:block`.
  Rows need the 44px floor on Join; the create form is already `flex-col`.
- **`/room`** — Now playing and Up next both fine as-is; artwork `size-20` → `size-16`
  and the Up-next `Play` button to the tap floor.
- **dock `<aside>`** — `bottom-20 top-1/3` gives a 166px sheet on a 500px-tall
  viewport. Compact becomes a real bottom sheet: `inset-x-0 bottom-0
h-[70dvh] rounded-b-none`, `sm:` keeps the current right-edge dock.

## Phase 4 — micro

One block, `Player.svelte` plus a handful of `micro:` classes.

- Player: `micro:fixed micro:inset-0 micro:rounded-none micro:border-0`, cover
  as a `background-image` with a `linear-gradient` scrim over it.
- `TrackInfo`: `micro:` centres it, hides the thumbnail (it is the background
  now), title in Golos not Unbounded.
- `Controls`: unchanged markup, `micro:` scales play to 32px and pushes the
  three buttons to `justify-center gap-6`.
- `SeekBar`: `micro:absolute micro:inset-x-0 micro:bottom-0 micro:h-1` with a
  `-inset-y-2` invisible hit area. The rain gauge column stops being a separate
  element and becomes the rail's own fill state.
- Room rail: extract `SessionStrip`'s `.rail` + `data-hold` into a 1px div the
  player renders at `micro:` only. Same three colours, same transition.
- `@media (max-width: 260px)`: `Backward`, `Forward`, and both timestamps to
  `display:none`.

## Order and verification

Phases are independent except that 0 precedes 3 (pages cannot drop their
padding until the player docks). 1 is the only one that fixes something broken
rather than something cramped — do it first if anything gets cut.

- `npm run test` and `npm run type-check` after each phase.
- Chrome DevTools at 350×200, 320×568, 390×844, and 640×360 landscape.
- In the Discord activity: launch, confirm the compact header fits, then pop to
  PiP and confirm the room stays connected — that is the `micro:hidden`-not-
  unmounted claim, and it is the one thing a viewport resize in a browser tab
  will not catch.

## Completion update

All five phases are in. `npm run test` (84 passing, including the new
`src/lib/sliderInteractions.test.ts`), `type-check`, `lint`, and `build` are
clean, and the three shapes were checked in a real browser at 1280×800,
390×844, 350×200, and 300×180 with a track loaded.

Four things landed differently from the draft above:

- **Micro is plain CSS, not `micro:` utilities.** A 700×300 window matches both
  `micro` and `sm`, and nothing guarantees which variant Tailwind emits last.
  Every micro rule is now anchored on an id (`#player`, `#track-info`,
  `#controls`, `#seekbar`) in one media block at the foot of `app.css`, so it
  outranks any utility on specificity regardless of order. The `micro:`
  variant survives only for `micro:hidden` on the header, the session strip,
  and the page wrapper, where nothing competes with it.
- **No 260px breakpoint.** Prev and the timestamps still fit at 300×180 with
  room to spare, so the third breakpoint never earned itself.
  → add it if a frame under ~260px turns up.
- **The dock buttons stayed as three targets rather than collapsing behind a
  `⌄`.** Chat, queue, and format are one tap away and cost less code than the
  menu that would hide them. Only volume was dropped on compact.
- **Two touch bugs came out with the slider fix.** `Queue`'s remove button and
  `Song`'s add button were `opacity-0` until `:hover`, so neither could be
  reached without a pointer at all. Both now show on compact and keep the
  hover reveal from `sm`.

One unrelated fix fell out of the build: Tailwind v4 scans markdown, so the
class names quoted in this file were being compiled into real CSS. `app.css`
now carries `@source not "../**/*.md"`.
