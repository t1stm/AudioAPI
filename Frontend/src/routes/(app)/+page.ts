import type { PageLoad } from './$types';
import { getRandomSongs, streamArtistLocal, streamRandomSongs } from '$requests/songs';
import { heroArtist } from '$lib/artists';

/**
 * Awaits nothing, so the route renders on the first frame and every section fills
 * itself in. The three requests also go out together now — the hero used to gate
 * them, which cost a whole round trip before anything was even asked for.
 *
 * A failed request has to be caught here: an awaited load lands on the error page,
 * but an unawaited promise would surface as an unhandled rejection. The generators
 * are caught by the page instead, where the section they belong to can be emptied.
 */
export const load: PageLoad = ({ fetch }) => {
	const hero = getRandomSongs(fetch, 1)
		.then((songs) => songs[0] ?? null)
		.catch(() => null);

	return {
		hero,
		picks: streamRandomSongs(fetch),
		librarySongs: streamRandomSongs(fetch, 200),
		// needs the hero's name, so it is the one thing that still chains — but it no
		// longer holds up anything else
		artistSongs: hero.then((song) => (song ? streamArtistLocal(heroArtist(song.artist), fetch) : null))
	};
};
