import type { SearchResult } from '$states/search.svelte';

export async function getSearch(
	term: string,
	fetch: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>
) {
	const r = await fetch(`https://api.gergov.bg/Audio/Search?query=${encodeURI(term)}`);
	const data = await r.json();

	return data as SearchResult[]
}
