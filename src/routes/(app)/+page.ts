import type { PageLoad } from './$types';
import { getRandomSongs } from '$requests/songs';

export const load: PageLoad = async ({ fetch }) => {
	return {
		songs: await getRandomSongs(fetch)
	};
};
