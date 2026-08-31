import type { SearchResult } from '$states/search.svelte';
import { convertTimeSpanStringToSeconds } from '$lib';
import { rememberRecentlyPlayed } from '$lib/recentlyPlayed';
import quality from './quality.svelte';

class Current {
	name: string = $state('');
	artist: string = $state('');
	url: string = $state('');
	lengthSeconds: number = $state(0);
	thumbnail: string = $state('');

	set(now: SearchResult) {
		this.name = now.name;
		this.artist = now.artist;
		this.url = `https://api.gergov.bg/Audio/Download/${quality.codec}/${quality.bitrate}?id=${encodeURIComponent(now.id)}`;
		this.lengthSeconds = convertTimeSpanStringToSeconds(now.duration);
		this.thumbnail = now.thumbnailUrl ?? '/empty.png';
		rememberRecentlyPlayed(now);
	}

	clear() {
		this.name = '';
		this.artist = '';
		this.url = '';
		this.lengthSeconds = 0;
		this.thumbnail = '/empty.png';
	}
}

export default new Current();
