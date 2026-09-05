/**
 * Which service a track came from, read off the protocol its ID carries.
 *
 * The API never says this in a field of its own — the ID is the whole answer, and the platform
 * prefixes it uses are a stable part of the contract (see Backend/API.md). One function rather than
 * the `id.startsWith('audio://')` checks that used to be inlined per component: those quietly labelled
 * everything that was not the library "YouTube", which stopped being true the moment Deezer existed.
 */
export type SourceName = 'Local' | 'Deezer' | 'YouTube' | 'Unknown';

export type Source = {
	name: SourceName;
	/** Tailwind classes for the solid badge. Solid, because it sits over arbitrary album art. */
	badge: string;
};

const sources: { prefix: string; source: Source }[] = [
	{ prefix: 'audio://', source: { name: 'Local', badge: 'bg-gold text-dark-0' } },
	{ prefix: 'deezer://', source: { name: 'Deezer', badge: 'bg-deezer text-dark-0' } },
	{ prefix: 'yt://', source: { name: 'YouTube', badge: 'bg-ember text-dark-0' } }
];

/**
 * A platform nobody here knows about is labelled rather than hidden: a row that plays is a row worth
 * showing, and an honest "Unknown" is what says a new pod shipped before this list was updated.
 */
const unknown: Source = { name: 'Unknown', badge: 'bg-surface-400 text-dark-0' };

export function sourceOf(id: string | undefined | null): Source {
	return sources.find((entry) => id?.startsWith(entry.prefix))?.source ?? unknown;
}
