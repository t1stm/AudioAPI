# Plan: back closes what is open, and undoes what a roll threw away

> **Status:** implemented. `npm test` (125), `npm run lint`, `npm run type-check` and
> `npm run build` all pass, and the behaviour below was driven in Chrome against the dev
> server: nested folders close innermost-first, the dock closes without disturbing the roll
> under it, three rolls walk back and forward one at a time, a layer closed by its own
> button spends its own history entry, and leaving for `/browse` and coming back restores
> the roll rather than drawing a new one.
>
> Two things changed while building it — see *What the build changed* at the end.

## The problem, measured against the code

Every dismissable thing in this app is a local boolean, and nothing in the app listens for a back
gesture:

```
$ grep -rn "popstate\|pushState\|CloseWatcher" src
(nothing)
```

So on a phone — where back is a swipe from the edge and the primary way out of anything — back always
means "leave the page", no matter what is open on top of it:

| What is open | Where | What back does today | What it should do |
| --- | --- | --- | --- |
| The queue/chat sheet, a 70dvh bottom sheet | `routes/(app)/+layout.svelte:26` `dock` | leaves the page | close the sheet |
| An opened folder, nested arbitrarily deep | `components/browse/FolderRow.svelte:10` `open` | leaves the page | close the innermost folder |
| The "which copy do you want" fork on the hero | `routes/(app)/+page.svelte` `pending` | leaves the page | dismiss the fork |
| The format popover over the transport | `components/player/layers/quality/Quality.svelte:4` `open` | leaves the page | close the popover |
| The name/account panel | `components/header/Header.svelte:11` `editingName` | leaves the page | close the panel |
| The room rename field | `components/session/SessionStrip.svelte:11` `renaming` | leaves the page | close the field |
| The "save as playlist" name field | `components/queue/Queue.svelte:67` `naming` | leaves the page | close the field |
| Playlist rename / delete confirmation | `routes/(app)/playlist/+page.svelte:19-20` | leaves the page | close it |

And one case that is not a layer at all. On the home page, `rollAgain()` overwrites `hero`, resets
`artistSongs` and drops `variant`; `rollPicks()` refills all thirty curated slots. Both are
destructive and neither leaves a trace — the roll you liked is gone the moment you press the button
again, and back does not bring it back because nothing ever recorded that it existed.

Two different problems, then, and they need two different mechanisms that agree on one rule:

> **Back consumes the most recent thing that happened on this page. Only when there is nothing left
> to consume does it leave the page.**

## What the platform gives us, and why it is not enough

`CloseWatcher` looks like exactly this feature, natively: a watcher intercepts a *close request*,
closes the topmost watcher first, and needs no history entries at all. It is in Chromium 120+.

But a close request is Escape and the **Android** back gesture only — on desktop Chrome the back
button and Alt+Left still leave the page, which is the case this is here for. Using it would mean
two code paths that disagree with each other on the desktop half of the request, to save some
history entries. One path, the same behaviour everywhere, is the cheaper thing to own.

So: one history entry behind each open layer, and Escape handled next to it. The entries must not go
through `history.pushState` directly — SvelteKit's router keeps its own history index and a raw push
desynchronises it. Shallow routing (`pushState` from `$app/navigation`, read back through
`page.state`) is the supported way to put a state-only entry into history.

## Phase 1 — `src/lib/backWatcher.svelte.ts`

Three exports.

```ts
/** The whole call site in a component: `closeOnBack(() => open, () => (open = false));` */
export function closeOnBack(isOpen: () => boolean, close: () => void): void;

/** A history entry that stays on this page — an undo point, like a roll. */
export function pushPageState(state: Partial<App.PageState>): void;

/** Called once, from the app layout. */
export function watchBackNavigation(): void;
```

`closeOnBack` is one `$effect` that opens a layer while `isOpen()` is true and closes it otherwise.
That is the entire integration cost for eight of the nine cases in the table.

How it works:

- `page.state.depth` counts the entries this page has pushed without leaving itself. Every layer and
  every undo point increments it, which is what makes one back press take the most recent of the two
  rather than closing a layer that has been open since before the undo point.
- Opening a layer records the depth it opened at and pushes an entry carrying it.
- A layer that closes itself — its own button, Escape, unmounting — spends its entry with
  `history.back()` when that entry is still the newest, so closing the dock and then pressing back
  leaves the page instead of paying for a layer that is already gone.
- Closing a layer that is *not* the newest (the dock, while a folder is open below it) leaves its
  entry behind. Back then closes the folder, and one later press lands on the spent entry and does
  nothing visible. Deliberate: the alternative — collapsing every layer above it — closes things the
  user did not ask to close.
- Escape is a `keydown` listener on `window` that closes the innermost layer.

The stack itself lives in `src/lib/backStack.ts`, apart from the SvelteKit glue, so the ordering —
the only part of this that can be got wrong — is testable without a router or a history.

One `$effect` in `routes/(app)/+layout.svelte`, through `watchBackNavigation()`, reads
`page.state.depth` and closes every layer opened above where a pop landed.

`App.PageState` in `src/app.d.ts` gains the two keys this plan uses:

```ts
interface PageState {
    depth?: number;
    home?: number;
}
```

**Test** — `src/lib/backStack.test.ts`, against the stack directly (no jsdom history traversal,
which is not reliable enough to assert on):

- three layers, back arrives three times: closes the innermost, then the middle, then the outermost.
- a layer that closed itself is skipped, and the one *above* it still closes on the next press.
- a layer already taken by a back press reports that it was gone, so nothing closes twice.
- the innermost layer is what Escape would reach.

## Phase 2 — wire the layers

One line each, at the top of the component's script where the boolean is declared:

| File | Line |
| --- | --- |
| `routes/(app)/+layout.svelte` | `closeOnBack(() => dock !== null, () => (dock = null));` |
| `components/browse/FolderRow.svelte` | `closeOnBack(() => open, () => (open = false));` |
| `components/player/layers/quality/Quality.svelte` | `closeOnBack(() => open, () => (open = false));` |
| `components/header/Header.svelte` | `closeOnBack(() => editingName, () => (editingName = false));` |
| `components/session/SessionStrip.svelte` | `closeOnBack(() => renaming, () => (renaming = false));` |
| `components/queue/Queue.svelte` | `closeOnBack(() => naming, () => (naming = false));` |
| `routes/(app)/playlist/+page.svelte` | one for `renaming`, one for `confirmingDelete` |
| `routes/(app)/+page.svelte` | `closeOnBack(() => pending !== null, close);` — `close()` already restores focus to the button that was pressed |

`FolderRow` is recursive, so every open folder registers its own watcher and the stack orders them by
when they were opened. Opening `Artists → Boards → Trilogy` and pressing back three times walks back
out of the tree one level at a time, then the fourth press leaves the page. No change to `FolderRow`'s
loading or caching: a closed folder still keeps what it fetched.

The existing Escape handlers in `SessionStrip` (line 75), `Queue` (line 185) and the home page's
`escapeCloses` become redundant once the watcher handles Escape. Delete them in the same commit —
two handlers doing the same job is how one of them rots.

## Phase 3 — the roll is a history of rolls

The home page keeps every roll it has drawn, and history holds an index into that list.

New `src/lib/rollHistory.ts` — a pure list, so it is testable without a component:

```ts
export type Roll = {
    hero: SearchResult | null;
    artistSongs: (SearchResult | null)[];
    picks: (SearchResult | null)[];
    variant: LocalVariant | null;
};

/** Appends a roll and returns its index. */
export function record(roll: Roll): number;
/** The roll at `index`, or null if history does not go that far — a reload empties the list while the URL's state survives. */
export function at(index: number): Roll | null;
```

In `+page.svelte`:

- The roll the page landed on gets its index the first time a roll is pushed on top of it, by
  `replaceState` on the entry it is already sitting on — so arriving at the home page still costs
  exactly one history entry, and back from the first roll leaves the page.
- `rollAgain()` keeps the current `picks` array by reference, takes the new hero and a fresh
  `artistSongs`, records it, and `pushState('', { ...page.state, home: index })`.
- `rollPicks()` does the mirror image: same hero, same `artistSongs`, new picks.
- An `$effect` on `page.state.home` restores `hero`, `artistSongs`, `picks` and `variant` from
  `at(index)`. Back therefore restores the whole page as it stood, including the "Alternative found"
  tag and the row of the hero's other tracks — no refetch, because the arrays being restored are the
  same arrays the streams filled.
- `rollToken` is gone: the roll on screen is the token. A lookup that lands for a roll the user has
  since backed out of writes into that roll and leaves the page alone.
- If `at(index)` returns null — a reload, which keeps the history entry and empties the list — the
  page keeps the roll it just loaded rather than showing an empty hero.
- Coming back to the page from another route lands on the entry it left on, and that roll is still in
  memory, so it is put back instead of being replaced by the fresh one the load fetched.

Only the index goes into history state — a `SearchResult` never does. History state is
structured-cloned and persisted by the browser, and the rolls are already in memory.

Forward comes free: back to the previous roll, forward to the one you backed out of.

**Test** — `src/lib/rollHistory.test.ts`: `record` returns increasing indices, `at` returns what was
recorded, `at` past the end and `at(-1)` return null.

## Phase 4 — verification

Automated: `npm test`, `npm run lint`, `npm run type-check`, `npm run build`.

Manual, because this is a gesture feature and no unit test covers a gesture:

| Where | Check |
| --- | --- |
| Chrome desktop | Escape and Alt+Left and mouse button 4 each close the topmost layer; with nothing open, back leaves the page — **done** |
| Chrome Android | edge-swipe back closes the dock, then the folder, then leaves |
| iOS Safari | edge-swipe back does the same through the fallback shim |
| Firefox desktop | fallback shim: Escape and back both work |
| Discord activity, desktop | the shim's history entries do not confuse the activity; the layout's Discord `goto('/room')` still lands correctly |
| Discord activity, Android | back closes a layer before it closes the activity — **the one that may not work**: if Discord's client swallows the gesture before the iframe sees it, layers stay closable by Escape and by their own controls, and this plan records that limitation rather than fighting the host |
| Home page | roll three times, press back three times, get all three rolls back in order, then leave the page — **done**, and forward re-applies them |
| Home page | roll, open the dock, press back: the dock closes and the roll stays — **done** |
| Any browser | reload with a roll on screen: history claims an index this page load never drew, and the page keeps the roll it loaded — **done** |

## What this plan deliberately does not do

- **The artist page's library/YouTube tabs.** A tab is a view of the same page, not something opened
  on top of it. Back should leave the page.
- **`showPlayed` in the queue.** An inline expansion of a list, not a layer.
- **Focus traps, `inert`, `<dialog>`.** The dock and the popovers are not modal today and this plan
  does not make them modal.
- **Search terms and playlist ids.** Already in the URL; back already works on them.
- **A general navigation state manager.** Nine call sites, one line each, two small files. Anything
  more is a framework for a problem this size.

---

## What the build changed

**`CloseWatcher` is not used at all.** Planned as the primary path with a shim behind it; dropped on
the first test. It takes Escape and the Android back gesture, but not the desktop back button — the
half of the request it would have to serve — so keeping it meant two paths that behave differently
on desktop. The shim is the only path now, and that is written down as a `ponytail:` comment at the
top of `backWatcher.svelte.ts` so nobody adds the branch back for the entries it would save.

**Two effects wrote the state they were reading.** Both froze the tab on the first click, and both
are the same mistake in different clothes:

- `closeOnBack` read `page.state.depth` to work out where it was, then pushed a new depth — so the
  effect depended on what it wrote and re-ran forever. Everything but `isOpen()` is now inside
  `untrack`.
- The home page's first roll called `replaceState` from `onMount`, which throws `Cannot call
  replaceState(...) before router is initialized` on a cold load — and took the rest of `onMount`
  with it, so the hero never arrived. The landing roll gets its index on the first push instead, by
  which time the router has long been up.
