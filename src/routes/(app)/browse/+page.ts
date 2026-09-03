import type { PageLoad } from './$types';
import { getBrowse } from '$requests/songs';

export const load: PageLoad = async ({ fetch }) => {
	// The root only. Every folder below it is fetched by the row that opens it.
	return { root: await getBrowse('', fetch) };
};
