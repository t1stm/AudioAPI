import type { SearchResult } from '$states/search.svelte';

export async function getSearch(
	term: string,
	fetch: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>
) {
	const r = await fetch(`https://api.gergov.bg/Audio/Search?query=${encodeURIComponent(term)}`);
	if (!r.ok) throw new Error(`The audio service returned ${r.status}.`);
	const data = await r.json();

	return data as SearchResult[];
}
