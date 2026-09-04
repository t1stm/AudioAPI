export type SearchResult = {
	id: string;
	name: string;
	artist: string;
	album?: string;
	/** Absent on room queue items — those carry the raw platform result. */
	contentUrl?: string;
	duration: string;
	thumbnailUrl: string | null;
};

const initialState: SearchResult[] = [];
export const searchResults = $state(initialState);
