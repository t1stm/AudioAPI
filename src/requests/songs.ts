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

/** Where the player streams a track from, at the quality selected right now. */
export function downloadUrl(id: string) {
	return `${audioApi}/Download/${quality.codec}/${quality.bitrate}?id=${encodeURIComponent(id)}`;
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

/**
 * The next track's body, downloaded while the current one is still playing.
 *
 * `preloadSong` above only warms the encode; the request for the bytes still
 * happens at the moment of the switch, and that request is the audible part of a
 * track change. This holds the finished bytes as an object URL instead, so the
 * switch is a local resource swap.
 *
 * One entry, not a cache: the only track anyone ever asks for is the next one.
 * That is a few megabytes at Opus 112 and tens of them at FLAC — still one track,
 * released the moment the next one is held. The key carries the quality, because
 * bytes encoded at the old bitrate are the wrong bytes once it changes.
 */
type Prefetch = { key: string; url: string; abort: AbortController };

let held: Prefetch | null = null;

function prefetchKey(id: string) {
	return `${quality.codec}/${quality.bitrate}/${id}`;
}

/** Starts holding `id`, dropping whatever was held. Cheap to call repeatedly:
 *  the track already held is a key comparison and no request. */
export function prefetchSong(id: string) {
	const key = prefetchKey(id);
	if (held?.key === key) return;
	dropPrefetch();

	const entry: Prefetch = { key, url: '', abort: new AbortController() };
	held = entry;

	fetch(downloadUrl(id), { signal: entry.abort.signal })
		.then((response) => (response.ok ? response.blob() : Promise.reject(response.status)))
		.then((blob) => {
			// superseded while in flight: the bytes are no longer the ones wanted, and
			// whoever superseded this already aborted and cleared it.
			if (held !== entry) return;
			entry.url = URL.createObjectURL(blob);
		})
		.catch(() => {
			// a prefetch that fails is a track that loads as slowly as it used to,
			// never a playback error.
			if (held === entry) held = null;
		});
}

/**
 * The held object URL for `id`, if that is what is held and it finished
 * downloading — and ownership of it with it, so the caller revokes it.
 *
 * A download still in flight for the track now starting is aborted rather than
 * waited on: the element is about to request the same bytes itself, and two
 * requests for one track is the opposite of the point.
 */
export function takePrefetched(id: string) {
	if (held?.key !== prefetchKey(id)) return null;

	const { url } = held;
	if (!url) held.abort.abort();
	held = null;

	return url || null;
}

/** Aborts what is in flight and revokes what is held. */
export function dropPrefetch() {
	if (!held) return;

	held.abort.abort();
	if (held.url) URL.revokeObjectURL(held.url);
	held = null;
}
