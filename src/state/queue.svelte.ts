import type { SearchResult } from '$states/search.svelte';
import current from './current.svelte';
import audio from './audio.svelte';

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
		if (this.remote) return this.remote(`add ${item.id}`);

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

	/** The Clear button: keep what is playing, drop everything around it. */
	clearOthers() {
		const now = this.items[this.currentIndex];
		this.items = now ? [now] : [];
		this.currentIndex = 0;
	}

	clear() {
		this.items = [];
		this.currentIndex = 0;
		audio.currentSeconds = 0;
		audio.bufferedSeconds = 0;
		audio.paused = true;
		current.clear();
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
