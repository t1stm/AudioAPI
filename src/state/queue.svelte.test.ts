import { describe, expect, it, beforeEach } from 'vitest';
import queue from './queue.svelte';
import current from './current.svelte';
import audio from './audio.svelte';
import type { SearchResult } from '$states/search.svelte';

function track(id: string): SearchResult {
	return { id, name: id, artist: 'artist', contentUrl: '', duration: '0:10', thumbnailUrl: null };
}

beforeEach(() => {
	queue.items = [track('a'), track('b'), track('c')];
	queue.currentIndex = 1; // 'b'
	audio.paused = false;
});

describe('previousTrack', () => {
	it('moves the index backward, not forward', () => {
		queue.previousTrack();
		expect(queue.currentIndex).toBe(0);
		expect(current.name).toBe('a');
	});
});

describe('removeItem', () => {
	it('removes index 0 instead of refusing (falsy-zero guard)', () => {
		queue.removeItem(queue.items[0]);
		expect(queue.items.map((i) => i.id)).toEqual(['b', 'c']);
	});

	it('is a no-op when the item is not in the queue', () => {
		queue.removeItem(track('missing'));
		expect(queue.items.map((i) => i.id)).toEqual(['a', 'b', 'c']);
	});
});

describe('removeIndex', () => {
	it('re-points playback when the removed track was current', () => {
		queue.removeIndex(1); // removes 'b', which was current
		expect(queue.items.map((i) => i.id)).toEqual(['a', 'c']);
		expect(current.name).toBe('c');
	});

	it('moves to the new last item when the removed track was current and last', () => {
		queue.currentIndex = 2; // 'c'
		queue.removeIndex(2);
		expect(queue.items.map((i) => i.id)).toEqual(['a', 'b']);
		expect(current.name).toBe('b');
	});
});

describe('nextTrack', () => {
	it('pauses instead of looping when it runs past the end of the queue', () => {
		queue.currentIndex = 2; // 'c', last item
		queue.nextTrack();
		expect(queue.currentIndex).toBe(2);
		expect(audio.paused).toBe(true);
	});

	it('advances and keeps playing mid-queue', () => {
		queue.nextTrack();
		expect(queue.currentIndex).toBe(2);
		expect(current.name).toBe('c');
		expect(audio.paused).toBe(false);
	});
});

describe('playNow', () => {
	it('inserts the track right after current and jumps to it', () => {
		queue.playNow(track('now'));
		expect(queue.items.map((i) => i.id)).toEqual(['a', 'b', 'now', 'c']);
		expect(queue.currentIndex).toBe(2);
		expect(current.name).toBe('now');
	});
});
