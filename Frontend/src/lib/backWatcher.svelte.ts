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
 * Closes the layer on a back gesture, the back button or Escape, for as long as it is open.
 * Call it once, beside the state it closes: `closeOnBack(() => open, () => (open = false));`
 */
export function closeOnBack(isOpen: () => boolean, close: () => void): void {
	// Whether the layer is open is the only thing this effect follows. Everything else it
	// touches is the history state it is itself pushing, and an effect that depends on what
	// it writes runs forever.
	$effect(() =>
		isOpen()
			? untrack(() => {
					const here = depth() + 1;
					const layer = push(here, close);
					pushState('', { ...page.state, depth: here });

					return () => {
						// The layer closed itself — its own button, Escape, or the route
						// unmounting. If the entry it pushed is still the newest one, closing has
						// to spend it, or the next back press pays for a layer that is gone.
						if (drop(layer) && here === depth()) history.back();
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
