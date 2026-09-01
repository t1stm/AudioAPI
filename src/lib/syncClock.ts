/**
 * Holding a room's clients on the server's clock.
 *
 * The room's shared position lives on the server as a monotonic stopwatch, and
 * `sync` is the only way to read it. The reply is already stale by the time it
 * lands, by however long the return trip took, so a client that compares its
 * position against the raw number is not measuring its offset — it is measuring
 * the difference between its uplink and its downlink, which is close to zero on
 * a symmetric path however far behind the room it actually is. That is why the
 * old half-second tolerance never fired even with clients a third of a second
 * apart: each one looked, honestly, fine to itself.
 *
 * Compensating for that is the whole feature. `reported + rtt/2` is the server's
 * position at the moment the reply arrived, and the difference against the local
 * position at that same moment is the real error.
 */

/** Samples the offset is drawn from. */
export const syncWindow = 16;
/** Rate correction per second of error. Doubles as the loop's time constant:
 *  the residual is erased over roughly 1/gain seconds. */
export const proportionalGain = 0.15;
/**
 * How far out the room may be before the loop touches the rate at all.
 *
 * Steering to zero sounds like a good idea and is not. The error estimate carries
 * a few milliseconds of jitter noise however well it is filtered, so a loop
 * aiming at zero never arrives: it hunts across 1.0 four times a second, and a
 * resampling ratio that never holds still is audible in a way that being twenty
 * milliseconds behind the room is not. Inside this band the rate is exactly 1
 * and stays there.
 */
export const syncDeadbandSeconds = 0.025;
/** The 98–102 % budget. 2 % is 34 cents of pitch; the loop only spends that
 *  much while more than 133 ms out, and holds within ±0.2 % (3.5 cents, under
 *  the JND) once settled. */
export const maxRateDeviation = 0.02;
/** Past this, steering is hopeless arithmetic — 2 % closes 20 ms a second — so
 *  jump instead and take the audible seam. */
export const hardSeekSeconds = 0.75;
/** The looser threshold that applies to the first reading of a track. A join or
 *  a track change starts everyone from a `seek` frame that was already one
 *  downlink stale, which on a slow link is a third of a second to steer off —
 *  fifteen seconds of it at 2 %. A jump in the first moment of a track is not
 *  audible the way fifteen seconds of being late is. */
export const firstReadingSnapSeconds = 0.05;
/** `sync` replies carry no request id, so exactly one may be in flight; this is
 *  the floor on top of that, not the interval. */
export const minSyncSpacingMs = 250;
/** Samples the drift regression runs over. Clock drift is tens of ppm — a 50 ppm
 *  device accumulates 3 ms a minute — so it does not rise out of the noise for
 *  several minutes. Read it, do not act on it: see `drift`. */
export const driftWindow = 240;
/** Below either of these the regression is fitting noise: a 50 ppm device
 *  accumulates 3 ms a minute, so a window shorter than this cannot separate it
 *  from the jitter it is buried in. */
export const driftMinimumSamples = 60;
export const driftMinimumSpanSeconds = 300;

export type SyncSample = { err: number; rtt: number };

const clamp = (value: number, low: number, high: number) =>
	Math.min(high, Math.max(low, value));

/** Least-squares slope of `open` against `at`, in seconds per second. */
function slope(history: { at: number; open: number }[]) {
	const n = history.length;
	const meanAt = history.reduce((sum, p) => sum + p.at, 0) / n;
	const meanOpen = history.reduce((sum, p) => sum + p.open, 0) / n;

	let covariance = 0;
	let variance = 0;
	for (const point of history) {
		covariance += (point.at - meanAt) * (point.open - meanOpen);
		variance += (point.at - meanAt) ** 2;
	}
	return variance === 0 ? 0 : covariance / variance;
}

export class SyncClock {
	/** The rate last handed out by `rateFor`, applied by the player. */
	rate = 1;

	private samples: SyncSample[] = [];
	/** Round trips alone, which survive `reset` because they measure the link,
	 *  not the track — and the seek lead is needed most in the moment right
	 *  after a track change, when there are no samples left to derive it from. */
	private trips: number[] = [];
	/** Correction already spent, so the device's own error can be recovered from
	 *  a loop whose entire job is hiding it. */
	private corrected = 0;
	private lastSampleAt: number | null = null;
	/** Whether this track has had its opening correction yet. */
	private settled = false;
	private history: { at: number; open: number }[] = [];

	/**
	 * One `sync` round trip.
	 *
	 * @param sentAt      client clock when the request went out, ms
	 * @param receivedAt  client clock when the reply landed, ms
	 * @param reported    the position the server replied with, seconds
	 * @param position    local audible position at `receivedAt`, seconds
	 * @returns how far behind the room this client is, seconds. Positive means
	 *          the room is ahead and the player has to catch up.
	 */
	sample(sentAt: number, receivedAt: number, reported: number, position: number) {
		if (!Number.isFinite(reported)) return this.error;

		const rtt = Math.max(0, receivedAt - sentAt) / 1000;
		// the reply describes the server one return trip ago; carry it to now
		this.samples.push({ err: reported + rtt / 2 - position, rtt });
		if (this.samples.length > syncWindow) this.samples.shift();
		this.trips.push(rtt);
		if (this.trips.length > syncWindow) this.trips.shift();

		const at = receivedAt / 1000;
		if (this.lastSampleAt !== null) this.corrected += (this.rate - 1) * (at - this.lastSampleAt);
		this.lastSampleAt = at;

		// error plus correction spent is the offset this device would have had
		// with no steering at all, so its slope is the device's clock rate error
		this.history.push({ at, open: this.error + this.corrected });
		if (this.history.length > driftWindow) this.history.shift();

		return this.error;
	}

	/**
	 * The least-queued sample of the window, which is NTP's trick for NTP's
	 * reason: a round trip can only ever be inflated, never shortened, so the
	 * fastest one seen is the one whose two halves are closest to equal, and the
	 * symmetry assumption is least wrong there. Averaging instead would fold
	 * every queueing spike straight into the estimate.
	 */
	get error() {
		if (this.samples.length === 0) return 0;
		return this.samples.reduce((best, s) => (s.rtt < best.rtt ? s : best)).err;
	}

	/** One-way latency estimate, seconds — what a `seek` frame is stale by. */
	get halfRtt() {
		if (this.trips.length === 0) return 0;
		return Math.min(...this.trips) / 2;
	}

	/**
	 * The device's clock rate error against the server's, as a fraction —
	 * positive when this device's audio clock runs fast, which is the sign the
	 * loop has to cancel: it settles on `1 - drift` to hold station. Diagnostics
	 * only, and `null` until there is enough history to mean anything.
	 *
	 * Nothing feeds this back into the rate, because the loop already absorbs
	 * drift: a proportional controller against a plant running `d` fast settles
	 * at `d / gain` of error, which is 0.8 ms at 120 ppm. An integral term for it
	 * would buy under a millisecond and cost the windup that comes with one.
	 */
	get drift(): number | null {
		const last = this.history.at(-1);
		if (last === undefined || this.history.length < driftMinimumSamples) return null;
		const span = last.at - this.history[0].at;
		if (span < driftMinimumSpanSeconds) return null;
		return -slope(this.history);
	}

	/**
	 * Whether this error wants a jump rather than the rate. True for anything the
	 * rate budget cannot close in reasonable time, and for the first reading of a
	 * track, where a jump costs nothing and steering costs a long slow arrival.
	 */
	shouldSeek(error: number) {
		const threshold = this.settled ? hardSeekSeconds : firstReadingSnapSeconds;
		return Math.abs(error) > threshold;
	}

	/**
	 * The playback rate that erases whatever of `error` lies outside the deadband,
	 * over roughly `1 / gain` seconds.
	 *
	 * A dead zone with slope rather than a switch: the correction eases in from
	 * exactly 1.0 at the edge of the band instead of stepping to it, so crossing
	 * the boundary is not itself the artifact the band exists to avoid. It also
	 * settles against the edge rather than driving through zero and back, which is
	 * the overshoot that made the rate visibly hunt.
	 */
	rateFor(error: number) {
		this.settled = true;
		const beyond = Math.sign(error) * Math.max(0, Math.abs(error) - syncDeadbandSeconds);
		this.rate = clamp(
			1 + proportionalGain * beyond,
			1 - maxRateDeviation,
			1 + maxRateDeviation,
		);
		return this.rate;
	}

	/** A track change or a join: the samples describe a position that no longer
	 *  exists, and the next reading is allowed its opening snap. The drift history
	 *  survives on purpose — it describes the device, not the track. */
	reset() {
		this.samples = [];
		this.rate = 1;
		this.lastSampleAt = null;
		this.settled = false;
	}

	/** After acting on `shouldSeek`. The window has to go with the position it
	 *  measured, or the same stale error is applied again on the next reading and
	 *  the correction runs away from itself; and this track has now had its one
	 *  free jump, so what follows is steering. */
	seeked() {
		this.samples = [];
		this.rate = 1;
		this.settled = true;
	}
}
