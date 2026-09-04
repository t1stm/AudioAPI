import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { SearchResult } from '$states/search.svelte';

const thumb = (thumbnailUrl: string | null, id = 'x'): SearchResult => ({
	id,
	name: 'x',
	artist: 'x',
	contentUrl: '',
	duration: '00:00:10',
	thumbnailUrl
});

async function loadAt(href: string) {
	vi.resetModules();
	vi.stubGlobal('location', new URL(href));
	return import('./discord');
}

beforeEach(() => {
	vi.unstubAllGlobals();
});

describe('discord activity detection', () => {
	it('leaves URLs alone in a normal browser tab', async () => {
		const { isDiscordActivity, audioApi, proxyThumbnails } = await loadAt('https://music.gergov.bg/');

		expect(isDiscordActivity).toBe(false);
		expect(audioApi).toBe('https://api.gergov.bg/Audio');
		expect(proxyThumbnails([thumb('https://i.ytimg.com/vi/abc/hq.jpg')])[0].thumbnailUrl).toBe(
			'https://i.ytimg.com/vi/abc/hq.jpg'
		);
	});

	it('detects the activity from the discordsays host and from ?frame_id', async () => {
		expect((await loadAt('https://1234.discordsays.com/')).isDiscordActivity).toBe(true);
		expect((await loadAt('https://music.gergov.bg/?frame_id=abc')).isDiscordActivity).toBe(true);
	});

	it('routes every thumbnail through the API cover endpoint', async () => {
		const { audioApi, proxyThumbnails } = await loadAt('https://1234.discordsays.com/');

		expect(audioApi).toBe('/.proxy/api/Audio');

		const [yt, cover, none] = proxyThumbnails([
			thumb('https://i9.ytimg.com/vi/abc/hq.jpg?sqp=1', 'yt://abc'),
			thumb('https://gergov.bg/Album_Covers/asdf.png', 'audio://asdf'),
			thumb(null, 'audio://bare')
		]);

		expect(yt.thumbnailUrl).toBe('/.proxy/api/Audio/Cover?id=yt%3A%2F%2Fabc');
		expect(cover.thumbnailUrl).toBe('/.proxy/api/Audio/Cover?id=audio%3A%2F%2Fasdf');
		expect(none.thumbnailUrl).toBe(null);
	});
});
