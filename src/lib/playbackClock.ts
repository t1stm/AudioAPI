/**
 * The position to report between the element's own `timeupdate` reports, from
 * the AudioContext clock: the anchor the element last gave us, plus the context
 * time elapsed since, less the latency the graph has not played out yet.
 */
export function interpolate(anchorMedia: number, elapsed: number, rate: number, latency: number) {
	return Math.max(anchorMedia + elapsed * rate - latency, 0);
}
