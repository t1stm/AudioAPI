/**
 * The layers open on top of the page, innermost last. A close request — Escape, a back
 * gesture, the back button — takes the last one and goes no further.
 *
 * Kept apart from the SvelteKit glue in `backWatcher.svelte.ts` so the ordering, which is
 * the only part that can be got wrong, is testable without a router or a history.
 */
export type Layer = {
	/** The history depth this layer was opened at; back drops everything above it. */
	depth: number;
	onclose: () => void;
};

const stack: Layer[] = [];

export function push(depth: number, onclose: () => void): Layer {
	const layer = { depth, onclose };
	stack.push(layer);
	return layer;
}

/**
 * Closes every layer opened above `depth`, innermost first. A layer that closed itself is
 * already gone from the stack, so nothing is closed twice.
 */
export function dropAbove(depth: number): void {
	while (stack.length > 0 && stack[stack.length - 1].depth > depth) stack.pop()!.onclose();
}

/** Forgets a layer that closed itself. False if a close request had already taken it. */
export function drop(layer: Layer): boolean {
	const index = stack.indexOf(layer);
	if (index === -1) return false;
	stack.splice(index, 1);
	return true;
}

/** The innermost open layer — what Escape closes. */
export function top(): Layer | null {
	return stack[stack.length - 1] ?? null;
}
