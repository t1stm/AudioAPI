import type { SearchResult } from '$states/search.svelte';
import { audioApi, proxyThumbnails } from '$lib/discord';
type Fetcher = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

export type QueryResolution =
	| { kind: 'local' | 'youtubeVideo'; query: string; result: SearchResult }
	| { kind: 'youtubePlaylist'; query: string; playlistId: string; results: SearchResult[] }
	| { kind: 'search'; query: string };

export class AudioApiError extends Error {
	constructor(message: string, readonly status: number) {
		super(message);
		this.name = 'AudioApiError';
	}
}

async function getJson<T>(fetcher: Fetcher, path: string): Promise<T> {
	const response = await fetcher(`${audioApi}${path}`);
	const payload = await response.json().catch(() => null);

	if (!response.ok) {
		const message = payload?.error?.message ?? `The audio service returned ${response.status}.`;
		throw new AudioApiError(message, response.status);
	}

	return payload as T;
}

async function getResults(fetcher: Fetcher, path: string) {
	return proxyThumbnails(await getJson<SearchResult[]>(fetcher, path));
}

export function getRandomSongs(fetcher: Fetcher, count = 30) {
	return getResults(fetcher, `/RandomResults?count=${count}`);
}

export function getArtistLocal(term: string, fetcher: Fetcher) {
	return getResults(fetcher, `/Artist/Local?term=${encodeURIComponent(term)}`);
}

export function getArtistYouTube(term: string, fetcher: Fetcher) {
	return getResults(fetcher, `/Artist/YouTube?term=${encodeURIComponent(term)}`);
}

export async function findQueryType(query: string, fetcher?: Fetcher) {
	const resolution = await getJson<QueryResolution>(
		fetcher ?? globalThis.fetch.bind(globalThis),
		`/FindQueryType?query=${encodeURIComponent(query)}`
	);

	if (resolution.kind === 'youtubePlaylist') resolution.results = proxyThumbnails(resolution.results);
	else if (resolution.kind !== 'search') resolution.result = proxyThumbnails([resolution.result])[0];

	return resolution;
}
