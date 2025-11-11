export const getRandomSongs = async (
	fetch: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>
) => {
	const request = await fetch('https://api.gergov.bg/Audio/RandomResults?count=30');
	return await request.json();
};
