import type { PageLoad } from './$types';
import { getSearch } from '$requests/search';

export const load: PageLoad = async ({ url, fetch }) => {
	const term = url.searchParams.get('term');
	if (!term) {
		return {
			results: []
		};
	}
	return {
		results: await getSearch(term, fetch)
	};
};
