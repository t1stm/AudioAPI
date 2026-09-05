/**
 * Every separator a credit can join its performers with. The comma is what the library writes,
 * the ampersand is what plenty of tags already carried — and it needs the spaces around it, so a
 * name like "Rad&Co" stays one artist. Same rule the backend splits on.
 */
const separators = /\s*,\s*|\s+&\s+/;

/**
 * The performers of a track, in order. The library joins every ARTISTS tag value into one
 * comma-separated string, so a credit like "Stiliyan, Jamaikata, Alex Toploto" is three artists —
 * each of which the artist page can be asked for on its own.
 */
export function splitArtists(artist: string | null | undefined): string[] {
	return (artist ?? '')
		.split(separators)
		.map((name) => name.trim())
		.filter((name) => name.length > 0);
}

/**
 * The one name to ask a whole-artist question with. The library matches an artist term against the
 * credit as a whole, so a joined one finds only the tracks credited to that exact line-up — the
 * lead artist is what "more from this artist" and "go to artist" mean.
 */
export function heroArtist(artist: string | null | undefined): string {
	return splitArtists(artist)[0] ?? '';
}
