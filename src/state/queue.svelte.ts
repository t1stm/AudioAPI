import type { SearchResult } from '$states/search.svelte';
import current from './current.svelte';
import audio from './audio.svelte';

class Queue {
	items: SearchResult[] = $state([]);
	currentIndex: number = $state(0);

	add(item: SearchResult) {
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
		if (!index) return;
		this.removeIndex(index);
	}

	removeIndex(index: number) {
		this.items.splice(index, 1);
		if (index < this.currentIndex) {
			this.currentIndex--;
		}
		if (index !== this.currentIndex) return;
	}

	setNext(targetIndex: number) {
		const items = this.items;

		if (targetIndex === this.currentIndex || targetIndex >= items.length || targetIndex < 0) return;

		if (this.currentIndex > targetIndex) this.currentIndex--;

		const removed = items.splice(targetIndex, 1);
		items.splice(this.currentIndex + 1, 0, removed[0]); // [0] is asserted above by only getting one
		this.items = items;
	}

	previousTrack() {
		if (this.items.length < 1) return;

		if (this.currentIndex > this.items.length) this.currentIndex = this.items.length - 1;

		if (this.currentIndex - 1 <= -1) {
			audio.currentSeconds = 0;
			return;
		}

		this.currentIndex += 1;
		this.setCurrent();
	}

	nextTrack() {
		if (this.currentIndex + 1 >= this.items.length) {
			audio.currentSeconds = current.lengthSeconds;
			return;
		}
		if (this.items.length < 1) return;

		this.currentIndex += 1;
		this.setCurrent();
	}

	setCurrent() {
		console.log(this);
		audio.currentSeconds = 0;
		const now = this.items[this.currentIndex];
		current.set(now);
	}
}

export default new Queue();
