import { untrack } from 'svelte';
import { pushState } from '$app/navigation';
import { page } from '$app/state';
import { drop, dropAbove, push, top } from './backStack';

/**
 * Back closes what is open before it leaves the page: one history entry behind every
 * layer, so the back button, a back gesture and Escape all take the innermost one first.
 *
 * The entries go through SvelteKit's `pushState` rather than `history.pushState`, because
 * the router keeps its own index into history and a raw push desynchronises it.
 *
 * ponytail: no `CloseWatcher` branch. It is the native version of this and it is in
 * Chromium, but it takes only Escape and the Android back gesture — the desktop back
 * button still leaves the page, which is the case this is here for. One path, same
 * behaviour in every browser, is worth more than the entries it saves.
 */

/** How many entries this page has pushed without leaving itself — layers and undo points alike. */
const depth = () => page.state.depth ?? 0;

/**
 * A history entry that stays on this page: an undo point, like a roll of the home page.
 * It shares the layers' counter, so one back press always takes the most recent of the
 * two rather than closing a layer that has been open since before the undo point.
 */
export function pushPageState(state: Partial<App.PageState>): void {
	pushState('', { ...page.state, ...state, depth: depth() + 1 });
}

/**
 * An entry a layer gave up this tick and has not spent yet. Leaving the full player for the
 * queue sheet closes one layer and opens another in the same tick, and the net number of open
 * layers does not change — so the sheet takes the entry the player let go of instead of the
 * player spending it and the sheet pushing a new one. Without the hand-over the `history.back()`
 * lands on whichever entry exists by then and closes the layer that just opened.
 */
let released: number | null = null;

/**
 * Closes the layer on a back gesture, the back button or Escape, for as long as it is open.
 * Call it once, beside the state it closes: `closeOnBack(() => open, () => (open = false));`
 */
export function closeOnBack(isOpen: () => boolean, close: () => void): void {
	// Through a derived, so the effect follows whether the layer is open and not the state
	// the caller reads to decide it. Switching the dock from the queue to the chat leaves it
	// open throughout; an effect reading `dock` directly would tear its layer down and push a
	// new one for the switch, and the `history.back()` that teardown spends would then land on
	// the fresh entry and close the dock outright.
	const open = $derived(isOpen());

	// Everything else the effect touches is the history state it is itself pushing, and an
	// effect that depends on what it writes runs forever.
	$effect(() =>
		open
			? untrack(() => {
					// An entry another layer released this tick is already at the right depth;
					// pushing a second one for the same position is what leaves a back press
					// with nothing to close.
					const inherited = released;
					released = null;

					const here = inherited ?? depth() + 1;
					const layer = push(here, close);
					if (inherited === null) pushState('', { ...page.state, depth: here });

					return () => {
						// The layer closed itself — its own button, Escape, or the route
						// unmounting. A close request had already taken it if `drop` says so,
						// and then the entry is the back press's to spend, not ours.
						if (!drop(layer)) return;

						// Offered to a layer opening in the same tick, and spent once the tick
						// has settled with no taker. Deciding here and now cannot work: the
						// layer that wants it may not have opened yet.
						released = here;
						queueMicrotask(() => {
							if (released !== here) return;
							released = null;
							if (here === depth()) history.back();
						});
					};
				})
			: undefined
	);
}

/** The other half of it, called once from the app layout. */
export function watchBackNavigation(): void {
	// Back is a drop in the depth: everything opened above where we landed closes.
	$effect(() => dropAbove(depth()));

	$effect(() => {
		const escape = (event: KeyboardEvent) => {
			if (event.key === 'Escape') top()?.onclose();
		};
		window.addEventListener('keydown', escape);
		return () => window.removeEventListener('keydown', escape);
	});
}
