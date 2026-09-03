import { describe, expect, it, beforeEach, vi } from 'vitest';
import { dropPrefetch, prefetchSong, takePrefetched } from './songs';
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
