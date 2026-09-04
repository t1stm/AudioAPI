import type { PageLoad } from './$types';
import { streamSearch } from '$requests/search';

/** Awaits nothing: the page renders its rows as placeholders and fills them in. */
export const load: PageLoad = ({ url, fetch }) => {
	const term = url.searchParams.get('term');

	return {
		term: term ?? '',
		results: term ? streamSearch(term, fetch) : null
	};
};
