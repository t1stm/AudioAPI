import type { LocalVariant } from '$requests/songs';
import type { SearchResult } from '$states/search.svelte';

/**
 * A roll of the home page: the hero, the row of its artist's other tracks, the picks
 * below it, and what the library had to say about the hero. Rolling again used to
 * overwrite all four, so the roll you liked was gone the moment you pressed the button.
 * Now every roll is kept and history holds an index into the list, which is what lets
 * back put the previous one back on screen.
 *
 * The arrays are the page's own reactive arrays, still being filled by their streams —
 * held by reference on purpose, so a roll restored mid-stream keeps filling.
 */
export type Roll = {
	hero: SearchResult | null;
	artistSongs: (SearchResult | null)[];
	picks: (SearchResult | null)[];
	variant: LocalVariant | null;
};

// ponytail: one list for the life of the tab, never trimmed. A roll is four references,
// and rolling a thousand times by hand is not a thing anyone does.
const rolls: Roll[] = [];

/** Keeps a roll and returns the index history will remember it by. */
export function record(roll: Roll): number {
	return rolls.push(roll) - 1;
}

/**
 * The roll at `index`, or null when history reaches past what this page load has drawn —
 * a reload keeps the history entry and empties the list.
 */
export function at(index: number): Roll | null {
	return rolls[index] ?? null;
}
