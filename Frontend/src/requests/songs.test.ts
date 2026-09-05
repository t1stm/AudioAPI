import { describe, expect, it, beforeEach, vi } from 'vitest';
import { dropPrefetch, findQueryType, isPlaylist, prefetchSong, streamResults, takePrefetched } from './songs';
import quality from '$states/quality.svelte';

const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

let blobs = 0;
let signals: AbortSignal[] = [];

beforeEach(() => {
	blobs = 0;
	signals = [];
	quality.codec = 'Opus';
	quality.bitrate = 112;
	URL.createObjectURL = vi.fn(() => `blob:${++blobs}`);
	URL.revokeObjectURL = vi.fn();
	vi.stubGlobal(
		'fetch',
		vi.fn((_url: string, init?: RequestInit) => {
			if (init?.signal) signals.push(init.signal);
			return Promise.resolve(new Response('bytes'));
		})
	);
	dropPrefetch();
});

describe('prefetchSong', () => {
	it('hands the held track over once, and only once', async () => {
		prefetchSong('a');
		await flush();

		expect(takePrefetched('a')).toBe('blob:1');
		expect(takePrefetched('a')).toBeNull();
	});

	it('is a miss at a quality the held bytes were not encoded at', async () => {
		prefetchSong('a');
		await flush();

		quality.bitrate = 320;
		expect(takePrefetched('a')).toBeNull();
	});

	it('does not re-request the track it is already holding', async () => {
		prefetchSong('a');
		await flush();
		prefetchSong('a');

		expect(fetch).toHaveBeenCalledTimes(1);
	});

	it('aborts and revokes the old track when the next one changes', async () => {
		prefetchSong('a');
		await flush();
		prefetchSong('b');

		expect(signals[0].aborted).toBe(true);
		expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:1');
		expect(takePrefetched('a')).toBeNull();
	});

	it('aborts a download still in flight for the track now starting', () => {
		prefetchSong('a');

		expect(takePrefetched('a')).toBeNull();
		expect(signals[0].aborted).toBe(true);
	});

	it('holds nothing after a failed download', async () => {
		vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new Error('offline'))));
		prefetchSong('a');
		await flush();

		expect(takePrefetched('a')).toBeNull();
	});
});

describe('findQueryType', () => {
	/** A Spotify playlist resolves to a playlist, not to a single track. Reading it as one put an
	 *  `undefined` into the queue. The resolution now carries no entries at all — `query` is what
	 *  the caller streams from — so the kind is the only thing that says which branch to take. */
	it('reads both playlist kinds as playlists', async () => {
		for (const kind of ['youtubePlaylist', 'spotifyPlaylist'] as const) {
			vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(Response.json({ kind, query: 'q', playlistId: 'p' }))));

			const resolution = await findQueryType('anything');

			expect(isPlaylist(resolution)).toBe(true);
			expect(isPlaylist(resolution) && resolution.playlistId).toBe('p');
		}
	});

	it('leaves a single result alone', async () => {
		vi.stubGlobal(
			'fetch',
			vi.fn(() => Promise.resolve(Response.json({ kind: 'youtubeVideo', query: 'q', result: { id: 'yt://a' } })))
		);

		const resolution = await findQueryType('anything');

		expect(isPlaylist(resolution)).toBe(false);
	});
});

describe('streamResults', () => {
	/** Cancelling the paste box aborts the stream mid-flight; the abort has to reach the caller as
	 *  a throw, or a cancelled playlist would look exactly like one that ended. */
	it('surfaces an abort rather than ending quietly', async () => {
		const controller = new AbortController();
		vi.stubGlobal('fetch', (_url: string, init?: RequestInit) =>
			Promise.resolve(
				new Response(
					new ReadableStream({
						start(stream) {
							stream.enqueue(new TextEncoder().encode('[{"id":"yt://a"}'));
							init?.signal?.addEventListener('abort', () => stream.error(new DOMException('Aborted', 'AbortError')));
						}
					})
				)
			)
		);

		const stream = streamResults((url, init) => fetch(url, { ...init, signal: controller.signal }), '/Search?query=q');
		expect((await stream.next()).value).toMatchObject({ id: 'yt://a' });

		controller.abort();
		await expect(stream.next()).rejects.toThrow('Aborted');
	});
});
