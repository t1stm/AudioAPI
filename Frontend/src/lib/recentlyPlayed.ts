import { browser } from '$app/environment';
import type { SearchResult } from '$states/search.svelte';

const storageKey = 'musicrain.recently-played';
const maximumEntries = 12;

export function getRecentlyPlayed(): SearchResult[] {
	if (!browser) return [];

	try {
		const stored = JSON.parse(localStorage.getItem(storageKey) ?? '[]');
		return Array.isArray(stored) ? (stored as SearchResult[]).slice(0, maximumEntries) : [];
	} catch {
		return [];
	}
}

export function rememberRecentlyPlayed(track: SearchResult) {
	if (!browser) return;

	const recent = getRecentlyPlayed().filter((item) => item.id !== track.id);
	localStorage.setItem(storageKey, JSON.stringify([track, ...recent].slice(0, maximumEntries)));
}
