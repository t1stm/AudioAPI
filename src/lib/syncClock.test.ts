import { describe, expect, it } from 'vitest';
import {
	SyncClock,
	driftMinimumSamples,
	hardSeekSeconds,
	minSteeringSamples,
	maxDeadbandSeconds,
	maxRateDeviation,
	proportionalGain,
	syncDeadbandSeconds,
	settledSyncSpacingMs,
	minSyncSpacingMs,
	syncTargetSeconds
} from './syncClock';

/** Any constant offset between the server's UTC clock and this client's
 *  `performance.now()`. The estimator has to work it out, so its value is
 *  arbitrary — which is the point of using an implausible one. */
const serverEpoch = 1_700_000_000_000;

/** A round trip whose reply carries the moment the server read it, `upMs` into a
 *  trip that takes `rttMs` in all. */
const stamped = (clock: SyncClock, at: number, rttMs: number, upMs: number, reported: number, position: number) =>
	clock.sample(at, at + rttMs, reported, position, serverEpoch + at + upMs);

/** One round trip: request out at `at`, reply back `rtt` later. */
const trip = (clock: SyncClock, at: number, rtt: number, reported: number, position: number) =>
	clock.sample(at, at + rtt, reported, position);

/** A clock with a full-enough window of clean, identical trips: the rate loop
 *  refuses to steer on an unfiltered window, and identical trips keep the band at
 *  its floor so these cases stay about the rate rather than about the link. */
const primed = () => {
	const clock = new SyncClock();
	for (let i = 0; i < minSteeringSamples; i++) trip(clock, i * 250, 20, 10, 10);
	return clock;
};

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

	/** `count` trips whose round trip alternates between `rttMs` and `rttMs + spread`. */
	const jittery = (clock: SyncClock, rttMs: number, spread: number, count = 8) => {
		for (let i = 0; i < count; i++) trip(clock, i * 250, rttMs + (i % 2) * spread, 10, 10);
	};

	it('will not steer on a window with nothing filtered out of it yet', () => {
		const clock = new SyncClock();
		clock.seeked();
		trip(clock, 0, 20, 10, 9.7); // 300 ms out on a single unfiltered reading
		expect(clock.rateFor(clock.error)).toBe(1);
	});

	it('holds the band at the floor on a link whose trips barely move', () => {
		const clock = new SyncClock();
		jittery(clock, 20, 1);
		expect(clock.deadband).toBe(syncDeadbandSeconds);
	});

	it('opens the band to the noise a jittery link puts in the estimate', () => {
		const clock = new SyncClock();
		// a round trip that wanders by 200 ms makes the error estimate wander too, and
		// the floor would be crossed by that noise alone several times a second
		jittery(clock, 400, 200);
		expect(clock.deadband).toBeCloseTo(0.2);
	});

	it('leaves the rate alone inside the band its own link asked for', () => {
		const clock = new SyncClock();
		jittery(clock, 400, 200);
		// 100 ms out, which the floor would have steered on and this link cannot
		// measure well enough to be worth steering on
		expect(clock.rateFor(0.1)).toBe(1);
	});

	it('holds the floor until there are trips enough to measure dispersion from', () => {
		const clock = new SyncClock();
		jittery(clock, 400, 200, 2);
		expect(clock.deadband).toBe(syncDeadbandSeconds);
	});

	it('never opens the band as far as the jump it is supposed to prevent', () => {
		const clock = new SyncClock();
		jittery(clock, 100, 5_000);
		expect(clock.deadband).toBe(maxDeadbandSeconds);
		// otherwise a client could sit at the edge of an audible seek and never be
		// steered off it
		expect(maxDeadbandSeconds).toBeLessThan(hardSeekSeconds);
	});

	it('leaves the rate alone while the room is together enough', () => {
		const clock = new SyncClock();
		// chasing the last few milliseconds costs a resampling ratio that never
		// holds still, which is the audible thing here — not the offset
		expect(clock.rateFor(syncDeadbandSeconds - 0.001)).toBe(1);
		expect(clock.rateFor(-syncDeadbandSeconds + 0.001)).toBe(1);
		expect(clock.rateFor(0)).toBe(1);
	});

	it('holds on past the band it engaged at, and lets go at the target', () => {
		const clock = primed();
		expect(clock.rateFor(syncDeadbandSeconds - 0.001)).toBe(1); // never engaged
		expect(clock.rateFor(syncDeadbandSeconds + 0.001)).not.toBe(1); // over the line

		// the whole point: back inside the band is not enough to let go, or the loop
		// would rest on the threshold and the estimate's jitter would work the rate
		expect(clock.rateFor(syncDeadbandSeconds - 0.001)).not.toBe(1);
		expect(clock.rateFor(syncTargetSeconds)).toBe(1);
		expect(clock.rateFor(syncDeadbandSeconds - 0.001)).toBe(1);
	});

	it('steers on the whole error once engaged, inside the 98–102 % budget', () => {
		const clock = primed();
		expect(clock.rateFor(0.1)).toBeCloseTo(1 + proportionalGain * 0.1);
		expect(clock.rateFor(-0.1)).toBeCloseTo(1 - proportionalGain * 0.1);
	});

	it('never spends more than the budget, however far out it is', () => {
		const clock = primed();
		expect(clock.rateFor(30)).toBeCloseTo(1 + maxRateDeviation);
		expect(clock.rateFor(-30)).toBeCloseTo(1 - maxRateDeviation);
	});

	it('offers the one-way latency a seek frame is stale by', () => {
		const clock = new SyncClock();
		trip(clock, 0, 400, 10, 10);
		trip(clock, 0, 120, 10, 10);
		expect(clock.halfRtt).toBeCloseTo(0.06);
	});

	it("works out where the server's clock stands from a stamped reply", () => {
		const clock = new SyncClock();
		stamped(clock, 0, 200, 100, 10, 10);
		expect(clock.skew).toBeCloseTo(serverEpoch);
	});

	it('knows nothing of the server clock until a reply carries one', () => {
		const clock = new SyncClock();
		trip(clock, 0, 200, 10, 10);
		expect(clock.skew).toBeNull();
		// and a frame it cannot place exactly still gets the best guess going
		expect(clock.stalenessOf(serverEpoch, 0)).toBeCloseTo(clock.halfRtt);
	});

	it('places a stamped frame by the flight it took, not the quickest one seen', () => {
		const clock = new SyncClock();
		stamped(clock, 0, 200, 100, 10, 10);

		// a broadcast that spent 300 ms getting here, on a link whose best trip is
		// 200 ms: half the round trip would have placed it 100 ms back and left this
		// client a fifth of a second ahead of the room
		const arrivedAt = 5_000;
		expect(clock.stalenessOf(serverEpoch + arrivedAt - 300, arrivedAt)).toBeCloseTo(0.3);
		expect(clock.halfRtt).toBeCloseTo(0.1);
	});

	it('refuses to place a frame in its own future', () => {
		const clock = new SyncClock();
		stamped(clock, 0, 200, 100, 10, 10);
		expect(clock.stalenessOf(serverEpoch + 5_400, 5_000)).toBe(0);
	});

	it('ages a sample forward by the correction spent since it was taken', () => {
		const clock = new SyncClock();
		clock.seeked(); // past the opening snap, so the rate is what corrects

		trip(clock, 0, 20, 10, 9.89); // 120 ms behind on the cleanest trip going
		// the loop will not steer on a window it cannot filter, so fill it out — a
		// touch slower, so they never displace the clean one, but close enough that
		// the link still reads as steady and the band stays at its floor
		for (let i = 1; i < minSteeringSamples; i++) trip(clock, i, 25, 10, 9.9);
		const rate = clock.rateFor(clock.error);

		// two and a bit seconds later, on a trip too queued to be trusted: the old
		// sample is still the one the estimator picks, and the loop has been closing
		// the gap the whole time it sat there
		trip(clock, 2_000, 400, 12.1, 12);

		const spent = (rate - 1) * (2.4 - (minSteeringSamples - 1 + 25) / 1000);
		expect(clock.error).toBeCloseTo(0.12 - spent);
		expect(spent).toBeGreaterThan(0.02);
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

	/**
	 * Two links of the same shape, one of which queues in bursts: 14 s at ~3 ms,
	 * then 4 s of 20-120 ms. The queue sits on the way back, which is the part that
	 * could hurt — `rtt / 2` credits half of a burst to an uplink that never spent
	 * it, so a burst reading on its own claims this client is tens of milliseconds
	 * ahead of a room it is sitting level with.
	 *
	 * Deterministic: a sinusoid whose period shares no factor with the cycle, so
	 * the trips wander without ever lining up against it.
	 */
	const quiet = (at: number) => {
		const rtt = 3 + 0.4 * Math.sin(at / 137);
		return { rtt, up: rtt / 2 };
	};
	const bursty = (at: number) => {
		if (at % 18_000 < 14_000) return quiet(at);
		const rtt = 70 + 50 * Math.sin(at / 137);
		return { rtt, up: 1.5 }; // the uplink is the same quiet trip; only the way back queues
	};

	/**
	 * The whole loop against one of those links, driven the way `session` drives
	 * it: poll, act on the reading, poll again at whichever spacing the loop's own
	 * state asks for. `at0` phases the link so a run can join mid-burst, and `ppm`
	 * is the device's own clock error — without one the loop has nothing to do and
	 * the link is never asked a hard question.
	 */
	const listen = (link: (at: number) => { rtt: number; up: number }, at0: number, ppm = 150) => {
		const clock = new SyncClock();
		let now = 0; // the client's `performance.now()`, ms
		let client = 100; // audible position, seconds
		let room = 100; // where the room actually is
		let seeks = 0;
		let worst = 0;
		let engagements = 0;
		let steering = false;

		const advance = (ms: number) => {
			now += ms;
			room += ms / 1000;
			client += (ms / 1000) * clock.rate * (1 + ppm * 1e-6);
		};

		while (now < 900_000) {
			const sentAt = now;
			const { rtt, up } = link(at0 + now);
			advance(up);
			const reported = room; // the server reads its stopwatch when the request lands
			advance(rtt - up);

			const error = clock.sample(sentAt, now, reported, client, serverEpoch + sentAt + up);
			if (clock.shouldSeek(error)) {
				client += error;
				clock.seeked();
				seeks++;
			} else {
				clock.rateFor(error);
			}
			if (clock.steering && !steering) engagements++;
			steering = clock.steering;
			worst = Math.max(worst, Math.abs(room - client));

			// `session.syncSpacing`: hard while it thinks it is chasing, idle once it is not.
			// ponytail: measured from the reply rather than the request as the real timer
			// does — 3 ms apart on the trips that decide anything here.
			const converging = clock.steering || Math.abs(error) > clock.deadband;
			advance(clock.settled && !converging ? settledSyncSpacingMs : minSyncSpacingMs);
		}
		return { seeks, engagements, worst };
	};

	it('rides out a link that queues in bursts as if it were a quiet one', () => {
		// A quarter of an hour on a 150 ppm device, which is the only thing in here
		// with real work for the loop: it walks the offset out to the band every few
		// minutes and the loop walks it back. The burst must not add to that.
		const still = listen(quiet, 0);
		expect(still.seeks).toBe(0);

		// including joining mid-burst, where the window opens on nothing but queued
		// trips and the first reading of a track is allowed to jump on 50 ms
		for (const at0 of [0, 14_500, 16_000]) {
			const run = listen(bursty, at0);
			expect({ at0, seeks: run.seeks }).toEqual({ at0, seeks: 0 });
			// the burst may not buy the loop extra work, nor cost accuracy for the
			// stillness: both of those are what a wider sample window trades away
			expect(run.engagements).toBeLessThanOrEqual(still.engagements + 1);
			expect(run.worst).toBeLessThan(still.worst + 0.005);
			expect(run.worst).toBeLessThan(maxDeadbandSeconds);
		}
	});
});
