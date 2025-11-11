import type { SearchResult } from '$states/search.svelte';
import { convertTimeSpanStringToSeconds } from '$lib';
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
		this.url = `https://api.gergov.bg/Audio/Download/${quality.codec}/${quality.bitrate}?id=${encodeURI(now.id)}`;
		this.lengthSeconds = convertTimeSpanStringToSeconds(now.duration);
		this.thumbnail = now.thumbnailUrl ?? '/static/empty.png';

    this.updateMediaSession(now)
	}

  updateMediaSession(now: SearchResult) {
    navigator.mediaSession.metadata = new MediaMetadata({
      title: now.name,
      artist: now.artist,
      artwork: [{ src: now.thumbnailUrl ?? '/static/empty.png' }]
    })
  }
}

export default new Current();
