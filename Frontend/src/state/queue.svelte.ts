import type { SearchResult } from '$states/search.svelte';
import current from './current.svelte';
import audio from './audio.svelte';
import { preloadSong } from '$requests/songs';

class Queue {
	items: SearchResult[] = $state([]);
	currentIndex: number = $state(0);

	/**
	 * Set by the session while it owns the room socket. Every verb below becomes
	 * a command and the server's broadcast writes the list back — nothing mutates
	 * locally, so the queue is never briefly a fiction. Commands are
	 * fire-and-forget: there is no acknowledgement and no error frame.
	 */
	remote: ((command: string) => void) | null = null;

	add(item: SearchResult) {
		if (this.remote) return this.remote(`add ${item.id}`);

		if (
			this.items.length > 0 &&
			this.currentIndex + 1 >= this.items.length &&
			audio.currentSeconds + 1 >= current.lengthSeconds
		) {
			this.items.push(item);
			this.nextTrack();
			return;
		}
		this.items.push(item);

		if (this.items.length !== 1) return;

		this.setCurrent();
	}

	removeItem(item: SearchResult) {
		const index = this.items.indexOf(item);
		if (index === -1) return;
		this.removeIndex(index);
	}

	removeIndex(index: number) {
		if (index < 0 || index >= this.items.length) return;
		if (this.remote) return this.remote(`remove ${index}`);
		const wasCurrent = index === this.currentIndex;

		this.items.splice(index, 1);
		if (index < this.currentIndex) {
			this.currentIndex--;
		}
		if (!wasCurrent) return;

		if (this.currentIndex >= this.items.length) {
			this.currentIndex = this.items.length - 1;
		}
		if (this.currentIndex < 0) return;
		this.setCurrent();
	}

	playNow(item: SearchResult) {
		// the protocol appends and jumps separately; the broadcast queue lands first
		if (this.remote) return this.remote(`add ${item.id}`);

		const insertAt = this.items.length > 0 ? this.currentIndex + 1 : 0;
		this.items.splice(insertAt, 0, item);
		this.currentIndex = insertAt;
		this.setCurrent();
	}

	playNext(item: SearchResult) {
		// `add` appends; `addnext` drops it in right after the current track, which is the
		// whole difference between this and the button above it
		if (this.remote) return this.remote(`addnext ${item.id}`);

		if (this.items.length === 0) {
			this.items.push(item);
			this.setCurrent();
			return;
		}

		this.items.splice(this.currentIndex + 1, 0, item);
	}

	playIndex(index: number) {
		if (index < 0 || index >= this.items.length) return;
		if (this.remote) return this.remote(`skipto ${index}`);
		this.currentIndex = index;
		this.setCurrent();
	}

	setNext(targetIndex: number) {
		if (this.remote) return this.remote(`setnext ${targetIndex}`);

		const items = this.items;

		if (targetIndex === this.currentIndex || targetIndex >= items.length || targetIndex < 0) return;

		if (this.currentIndex > targetIndex) this.currentIndex--;

		const removed = items.splice(targetIndex, 1);
		items.splice(this.currentIndex + 1, 0, removed[0]); // [0] is asserted above by only getting one
		this.items = items;
	}

	/** Drag-reorder: the track lands where it was dropped, rather than always next. */
	move(from: number, to: number) {
		if (this.remote) return this.remote(`move ${from} ${to}`);

		const items = this.items;
		if (from === to) return;
		if (from < 0 || from >= items.length || to < 0 || to >= items.length) return;

		const [moved] = items.splice(from, 1);
		items.splice(to, 0, moved);

		// Whatever was playing keeps playing: only its index moves, and only when the
		// track was carried across it.
		if (from === this.currentIndex) this.currentIndex = to;
		else if (from < this.currentIndex && to >= this.currentIndex) this.currentIndex--;
		else if (from > this.currentIndex && to <= this.currentIndex) this.currentIndex++;
	}

	shuffle() {
		if (this.remote) return this.remote('shuffle');

		const firstUpcoming = this.currentIndex + 1;
		if (this.items.length - firstUpcoming < 2) return;

		const shuffled = [...this.items.slice(firstUpcoming)];
		for (let index = shuffled.length - 1; index > 0; index--) {
			const target = Math.floor(Math.random() * (index + 1));
			[shuffled[index], shuffled[target]] = [shuffled[target], shuffled[index]];
		}

		this.items = [...this.items.slice(0, firstUpcoming), ...shuffled];
	}

	/**
	 * Play all: the list becomes the queue and the first track starts. In a room the
	 * queue is the server's, so the tracks are appended instead of replacing anything.
	 */
	replaceWith(items: SearchResult[]) {
		if (this.remote) {
			for (const item of items) this.remote(`add ${item.id}`);
			return;
		}
		if (items.length === 0) return;

		this.items = [...items];
		this.currentIndex = 0;
		this.setCurrent();
	}

	/** The Clear button: keep what is playing, drop everything around it. */
	clearOthers() {
		if (this.remote) return this.remote('clear');

		const now = this.items[this.currentIndex];
		this.items = now ? [now] : [];
		this.currentIndex = 0;
	}

	previousTrack() {
		if (this.remote) return this.remote('previous');
		if (this.items.length < 1) return;

		if (this.currentIndex > this.items.length) this.currentIndex = this.items.length - 1;

		if (this.currentIndex - 1 <= -1) {
			audio.currentSeconds = 0;
			return;
		}

		this.currentIndex -= 1;
		this.setCurrent();
	}

	/** Warms the encode for whatever plays next, so the switch does not wait on ffmpeg. */
	preloadNext() {
		const next = this.items[this.currentIndex + 1];
		if (next) preloadSong(next.id);
	}

	nextTrack() {
		if (this.remote) return this.remote('next');
		if (this.items.length < 1) return;

		if (this.currentIndex + 1 >= this.items.length) {
			audio.paused = true;
			return;
		}

		this.currentIndex += 1;
		this.setCurrent();
	}

	setCurrent() {
		audio.currentSeconds = 0;
		audio.paused = false;
		const now = this.items[this.currentIndex];
		current.set(now);
	}
}

export default new Queue();
