import type { PageLoad } from './$types';
import { getArtistLocal, getRandomSongs } from '$requests/songs';

export const load: PageLoad = async ({ fetch }) => {
	const hero = (await getRandomSongs(fetch, 1))[0] ?? null;
	const [songs, librarySongs, artistSongs] = await Promise.all([
		getRandomSongs(fetch),
		getRandomSongs(fetch, 200),
		hero ? getArtistLocal(hero.artist, fetch) : Promise.resolve([])
	]);

	return {
		hero,
		songs,
		librarySongs,
		artistSongs
	};
};
