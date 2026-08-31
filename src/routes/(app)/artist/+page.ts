import type { PageLoad } from './$types';
import { getArtistLocal, getArtistYouTube } from '$requests/songs';

export const load: PageLoad = async ({ url, fetch }) => {
	const term = url.searchParams.get('term')?.trim() ?? '';
	if (!term) return { term, localResults: [], youtubeResults: [] };

	const [localResults, youtubeResults] = await Promise.all([
		getArtistLocal(term, fetch),
		getArtistYouTube(term, fetch)
	]);

	return { term, localResults, youtubeResults };
};
