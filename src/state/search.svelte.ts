export type SearchResult = {
	id: string;
	name: string;
	artist: string;
	contentUrl: string;
	duration: string;
	thumbnailUrl: string | null;
};

const initialState: SearchResult[] = [];
export let searchResults = $state(initialState);
