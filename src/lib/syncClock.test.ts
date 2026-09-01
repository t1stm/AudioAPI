import { describe, expect, it } from 'vitest';
import {
	SyncClock,
	driftMinimumSamples,
	maxRateDeviation,
	proportionalGain,
	syncDeadbandSeconds,
} from './syncClock';

/** One round trip: request out at `at`, reply back `rtt` later. */
const trip = (clock: SyncClock, at: number, rtt: number, reported: number, position: number) =>
	clock.sample(at, at + rtt, reported, position);

describe('SyncClock', () => {
	it('credits the reply with the half trip it spent in flight', () => {
		const clock = new SyncClock();
		// server said 10 s, 200 ms ago; a client sitting at exactly 10 s is 100 ms behind
		expect(trip(clock, 0, 200, 10, 10)).toBeCloseTo(0.1);
	});

	it('reports no error for a client already the half trip ahead', () => {
		const clock = new SyncClock();
		expect(trip(clock, 0, 200, 10, 10.1)).toBeCloseTo(0);
	});

	it('takes the least-queued sample, not the newest or the average', () => {
		const clock = new SyncClock();
		trip(clock, 0, 40, 10, 10); //  20 ms behind, clean trip
		trip(clock, 100, 600, 10.5, 10.5); // 300 ms behind, one queued to death
		expect(clock.error).toBeCloseTo(0.02);
	});

	it('leaves the rate alone while the room is together enough', () => {
		const clock = new SyncClock();
		// chasing the last few milliseconds costs a resampling ratio that never
		// holds still, which is the audible thing here — not the offset
		expect(clock.rateFor(syncDeadbandSeconds - 0.001)).toBe(1);
		expect(clock.rateFor(-syncDeadbandSeconds + 0.001)).toBe(1);
		expect(clock.rateFor(0)).toBe(1);
	});

	it('eases in from 1.0 at the edge rather than stepping to a correction', () => {
		const clock = new SyncClock();
		expect(clock.rateFor(syncDeadbandSeconds + 0.0001)).toBeCloseTo(1, 4);
	});

	it('steers on what lies beyond the band, inside the 98–102 % budget', () => {
		const clock = new SyncClock();
		const beyond = 0.1 - syncDeadbandSeconds;
		expect(clock.rateFor(0.1)).toBeCloseTo(1 + proportionalGain * beyond);
		expect(clock.rateFor(-0.1)).toBeCloseTo(1 - proportionalGain * beyond);
	});

	it('never spends more than the budget, however far out it is', () => {
		const clock = new SyncClock();
		expect(clock.rateFor(30)).toBeCloseTo(1 + maxRateDeviation);
		expect(clock.rateFor(-30)).toBeCloseTo(1 - maxRateDeviation);
	});

	it('offers the one-way latency a seek frame is stale by', () => {
		const clock = new SyncClock();
		trip(clock, 0, 400, 10, 10);
		trip(clock, 0, 120, 10, 10);
		expect(clock.halfRtt).toBeCloseTo(0.06);
	});

	it('ignores a reply that did not parse as a number', () => {
		const clock = new SyncClock();
		trip(clock, 0, 200, 10, 10);
		expect(clock.sample(0, 200, Number.NaN, 10)).toBeCloseTo(0.1);
	});

	it('withholds a drift reading until the history can support one', () => {
		const clock = new SyncClock();
		trip(clock, 0, 20, 0, 0);
		expect(clock.drift).toBeNull();
	});

	it('withholds it from a dense window that is still far too short', () => {
		const clock = new SyncClock();
		// four samples a second for a minute: plenty of points, and still nowhere
		// near long enough for tens of ppm to rise out of the jitter
		for (let i = 0; i < 240; i++) trip(clock, i * 250, 20, i * 0.25, i * 0.25);
		expect(clock.drift).toBeNull();
	});

	it('recovers a fast device from the correction the loop spent hiding it', () => {
		const clock = new SyncClock();
		const ppm = 200e-6;
		// a device whose audio clock runs 200 ppm fast, with the loop holding it on
		// station: the error stays near zero and the commanded rate carries the
		// whole story, which is exactly what the open-loop view puts back
		for (let i = 0; i <= driftMinimumSamples; i++) {
			trip(clock, i * 5000, 20, i * 5, i * 5);
			clock.rate = 1 - ppm; // what the loop settles on to hold station
		}
		expect(clock.drift).toBeCloseTo(ppm, 5);
	});

	it('snaps once at the top of a track, then steers', () => {
		const clock = new SyncClock();
		expect(clock.shouldSeek(trip(clock, 0, 400, 10, 9.7))).toBe(true); // 500 ms out
		clock.seeked();
		expect(clock.shouldSeek(trip(clock, 500, 400, 10.5, 10.3))).toBe(false); // 400 ms out
	});

	it('still jumps for an error no rate could ever close', () => {
		const clock = new SyncClock();
		clock.seeked();
		expect(clock.shouldSeek(trip(clock, 0, 40, 90, 10))).toBe(true);
	});

	it('drops the window it just acted on, so a snap cannot repeat itself', () => {
		const clock = new SyncClock();
		trip(clock, 0, 400, 10, 9.7);
		clock.seeked();
		expect(clock.error).toBe(0);
	});

	it('forgets its samples on a track change but keeps knowing the link', () => {
		const clock = new SyncClock();
		trip(clock, 0, 200, 10, 10);
		clock.reset();
		expect(clock.error).toBe(0);
		expect(clock.rate).toBe(1);
		// the seek lead is needed in the very next frame the server sends
		expect(clock.halfRtt).toBeCloseTo(0.1);
	});
});
