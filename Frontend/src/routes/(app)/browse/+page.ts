import type { PageLoad } from './$types';
import { getBrowse } from '$requests/songs';

export const load: PageLoad = ({ fetch }) => {
	// The root only. Every folder below it is fetched by the row that opens it.
	// Not awaited: one level is one object, so there is nothing to fill in piece by
	// piece, but the page can still be on screen while it is on its way.
	return { root: getBrowse('', fetch).catch(() => null) };
};
