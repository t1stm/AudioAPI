import type { SearchResult } from '$states/search.svelte';
import { audioApi, proxyThumbnails } from '$lib/discord';
import quality from '$states/quality.svelte';
type Fetcher = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

export type QueryResolution =
	| { kind: 'local' | 'youtubeVideo'; query: string; result: SearchResult }
	| {
			kind: 'youtubePlaylist';
			query: string;
			playlistId: string;
			results: SearchResult[];
	  }
	| { kind: 'search'; query: string };

export class AudioApiError extends Error {
	constructor(
		message: string,
		readonly status: number
	) {
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

export function getRandomSongs(fetcher: Fetcher, count = 30, youTubeShare?: number) {
	const share = youTubeShare === undefined ? '' : `&youTubeShare=${youTubeShare}`;
	return getResults(fetcher, `/RandomResults?count=${count}${share}`);
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

export type LocalVariant = {
	/** `same` recording, a `variant` take, or a `weak` guess that says so. */
	match: 'same' | 'variant' | 'weak';
	score: number;
	/** Library minus upload. Uploads carry intros, so this is shown, not judged on. */
	durationDeltaSeconds: number;
	youTubeTags: string[];
	libraryTags: string[];
	result: SearchResult;
};

/**
 * What the library has to say about a YouTube result. The `yt://` guard is the whole
 * "don't ask about a roll that already came from the library" rule, at the one place
 * every caller goes through. A 204 arrives as null: getJson already swallows the
 * empty body on an ok response.
 */
export async function getLocalVariant(song: SearchResult, fetcher: Fetcher) {
	if (!song.id.startsWith('yt://')) return null;

	const query = `name=${encodeURIComponent(song.name)}&artist=${encodeURIComponent(song.artist)}&duration=${song.duration}`;
	const variant = await getJson<LocalVariant | null>(fetcher, `/Local/Variant?${query}`);
	if (variant) variant.result = proxyThumbnails([variant.result])[0];

	return variant;
}

/**
 * Asks the API to start the encode the player is about to request, without the
 * body. The API shares one encode per codec/bitrate/id, so the Download that
 * follows gets the finished audio, or joins the one still being produced.
 *
 * Fire and forget: a preload that fails is a track that starts as slowly as it
 * used to, never a playback error. A failed one drops out of the set so the next
 * trigger can try again.
 */
const preloaded = new Set<string>();

export function preloadSong(id: string) {
	const path = `/Preload/${quality.codec}/${quality.bitrate}?id=${encodeURIComponent(id)}`;
	if (preloaded.has(path)) return;
	preloaded.add(path);

	fetch(`${audioApi}${path}`).catch(() => preloaded.delete(path));
}
