import type { SearchResult } from '$states/search.svelte';
import { convertTimeSpanStringToSeconds } from '$lib';
import { rememberRecentlyPlayed } from '$lib/recentlyPlayed';
import { downloadUrl, takePrefetched } from '$requests/songs';

class Current {
	name: string = $state('');
	artist: string = $state('');
	url: string = $state('');
	lengthSeconds: number = $state(0);
	thumbnail: string = $state('');

	/** The object URL the current track is playing from, when it came from the
	 *  prefetch. Held so it can be revoked — on the *next* change, not this one:
	 *  the retry path in `Audio.svelte` re-requests the current resource, and a
	 *  revoked URL is not there to re-request. */
	#objectUrl = '';

	set(now: SearchResult) {
		this.#release();
		const prefetched = takePrefetched(now.id);
		this.#objectUrl = prefetched ?? '';

		this.name = now.name;
		this.artist = now.artist;
		this.url = prefetched ?? downloadUrl(now.id);
		this.lengthSeconds = convertTimeSpanStringToSeconds(now.duration);
		this.thumbnail = now.thumbnailUrl ?? '/empty.png';
		rememberRecentlyPlayed(now);
	}

	#release() {
		if (!this.#objectUrl) return;
		URL.revokeObjectURL(this.#objectUrl);
		this.#objectUrl = '';
	}

	clear() {
		this.#release();
		this.name = '';
		this.artist = '';
		this.url = '';
		this.lengthSeconds = 0;
		this.thumbnail = '/empty.png';
	}
}

export default new Current();
