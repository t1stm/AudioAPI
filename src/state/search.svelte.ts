export type SearchResult = {
	id: string;
	name: string;
	artist: string;
	album?: string;
	contentUrl: string;
	duration: string;
	thumbnailUrl: string | null;
};

const initialState: SearchResult[] = [];
export const searchResults = $state(initialState);
