import type { PageLoad } from './$types';
import { streamArtistLocal, streamArtistYouTube } from '$requests/songs';

/** Awaits nothing: both sides render as placeholder rows and fill themselves in. */
export const load: PageLoad = ({ url, fetch }) => {
	const term = url.searchParams.get('term')?.trim() ?? '';
	if (!term) return { term, localResults: null, youtubeResults: null };

	return {
		term,
		localResults: streamArtistLocal(term, fetch),
		youtubeResults: streamArtistYouTube(term, fetch)
	};
};
