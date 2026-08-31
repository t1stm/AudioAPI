import type { SearchResult } from '$states/search.svelte';
import { audioApi, proxyThumbnails } from '$lib/discord';

export async function getSearch(
	term: string,
	fetch: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>
) {
	const r = await fetch(`${audioApi}/Search?query=${encodeURIComponent(term)}`);
	if (!r.ok) throw new Error(`The audio service returned ${r.status}.`);
	const data = await r.json();

	return proxyThumbnails(data as SearchResult[]);
}
