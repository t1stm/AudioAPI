/**
 * A room full of simulated listeners, against a real Gaida.API.
 *
 * Headless clients speak the session protocol over real websockets, each behind
 * its own delay pipe and each carrying its own audio-clock drift, so the thing
 * being measured is the protocol and the correction loop rather than a browser.
 * The estimator is the real `src/lib/syncClock.ts`, not a copy.
 *
 *   node tools/roomSim.mjs                 the proposed loop
 *   MODE=naive node tools/roomSim.mjs      the half-second tolerance it replaces
 *   API=http://host:5226 SEED=7 …
 *
 * What comes out is the spread between clients — how far apart two people in the
 * room actually are — sampled 20 times a second through a scripted session.
 */
import { SyncClock, minSyncSpacingMs, settledSyncSpacingMs } from '../src/lib/syncClock.ts';

const API = process.env.API ?? 'http://localhost:5226';
const WS = API.replace(/^http/, 'ws');
const MODE = process.env.MODE ?? 'rate';
const SEED = Number(process.env.SEED ?? 1);

const now = () => Number(process.hrtime.bigint()) / 1e6;
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

/** Seeded, so a run that fails is a run that can be run again. */
let seed = SEED >>> 0 || 1;
function random() {
	seed ^= seed << 13; seed >>>= 0;
	seed ^= seed >> 17;
	seed ^= seed << 5; seed >>>= 0;
	return seed / 4294967296;
}

/** TCP does not reorder. A delay model that does would invent failures that
 *  cannot happen on the wire — the queue frame overtaking its own current. */
class Pipe {
	constructor(delay) { this.delay = delay; this.at = 0; }
	push(run) {
		this.at = Math.max(this.at, now() + this.delay());
		setTimeout(run, this.at - now());
	}
}

class Listener {
	/**
	 * @param ping     round trip, ms
	 * @param jitter   ± on each one-way leg, ms — the floor under the accuracy
	 * @param driftPpm this device's audio clock against real time, positive fast
	 * @param buffer   how long this client takes to answer the loading barrier
	 */
	constructor(name, { ping, jitter, driftPpm, buffer }) {
		Object.assign(this, { name, ping, jitter, driftPpm, buffer });
		this.position = 0;
		this.playing = false;
		this.rate = 1;
		this.lastTick = now();
		this.clock = new SyncClock();
		this.rtts = [];
		this.offsets = [];
		this.index = null;
		this.trackId = '';
		this.inFlight = false;
		this.seeks = 0;
		this.rates = [];
		this.rateTimes = [];
		this.loadedFor = new Set();
		this.up = new Pipe(() => this.oneWay());
		this.down = new Pipe(() => this.oneWay());
	}

	oneWay() { return Math.max(0.5, this.ping / 2 + (random() * 2 - 1) * this.jitter); }

	connect(room) {
		this.socket = new WebSocket(
			`${WS}/Audio/Multiplayer/Join?room=${room}&username=${encodeURIComponent(this.name)}`,
		);
		return new Promise(resolve => {
			this.socket.onopen = resolve;
			this.socket.onmessage = event =>
				this.down.push(() => this.receive(String(event.data)));
		});
	}

	send(command) {
		this.up.push(() => { try { this.socket.send(command); } catch { /* closed */ } });
	}

	/** The audio hardware: position advances at the commanded rate, off by the
	 *  device's own drift, and only while the element is really playing. */
	tick() {
		const at = now();
		const elapsed = (at - this.lastTick) / 1000;
		this.lastTick = at;
		if (this.playing) this.position += elapsed * this.rate * (1 + this.driftPpm / 1e6);
	}

	receive(raw) {
		this.tick();
		const space = raw.indexOf(' ');
		const command = space === -1 ? raw : raw.slice(0, space);
		const argument = space === -1 ? '' : raw.slice(space + 1);

		switch (command) {
			case 'queue':
				this.items = JSON.parse(argument);
				this.setCurrent(this.index ?? 0);
				break;
			case 'current':
				this.setCurrent(Number(argument));
				break;
			case 'playing':
				this.playing = argument.trim() === 'True';
				break;
			case 'seek': {
				// measured at broadcast, so one downlink stale by the time it lands.
				// The stamp says how stale; `stalenessOf` falls back to half a round
				// trip without one, which is what the naive mode is made of.
				const { seconds, stamp } = timed(argument);
				this.position = seconds + (MODE === 'rate' ? this.clock.stalenessOf(stamp, now()) : 0);
				break;
			}
			case 'sync': {
				const { seconds, stamp } = timed(argument);
				this.onSync(seconds, stamp);
				break;
			}
		}
	}

	onSync(reported, stamp) {
		const at = now();
		this.inFlight = false;
		this.rtts.push(at - this.sentAt);

		if (MODE === 'naive') {
			// what the room does today: compare against the raw reply and correct
			// only past half a second. It never fires.
			if (Math.abs(this.position - reported) > 0.5) { this.position = reported; this.seeks++; }
			return;
		}

		const error = this.clock.sample(this.sentAt, at, reported, this.position, stamp);
		if (this.clock.shouldSeek(error)) {
			this.position += error;
			this.seeks++;
			this.rate = 1;
			this.clock.seeked();
			return;
		}
		this.rate = this.clock.rateFor(error);
		this.rates.push(this.rate);
		this.rateTimes.push(now());
	}

	setCurrent(index) {
		const item = this.items?.[index];
		if (index === this.index && (item?.id ?? '') === this.trackId) return;

		this.index = index;
		this.trackId = item?.id ?? '';
		this.playing = false;
		this.position = 0;
		this.clock.reset();
		this.rate = 1;

		// Exactly once per track, as the barrier counts messages, not people —
		// and keyed on the item, not the index: an `add` to an idle room re-arms
		// the barrier on the same index 0 the empty room already handed out, so an
		// index-only guard sits silent and the room never starts.
		const key = `${index}:${this.trackId}`;
		if (this.loadedFor.has(key)) return;
		this.loadedFor.add(key);
		if (!item) return this.send('loaded');
		setTimeout(() => this.send('loaded'), this.buffer);
	}

	/** `sync` replies carry no request id, so only one may be outstanding. The
	 *  spacing backs off once the track has settled — same rule the browser runs,
	 *  so the sample rate the simulator measures is the one that ships. */
	startSync() {
		this.syncTimer = setInterval(() => {
			if (this.inFlight) return;
			const spacing =
				MODE === 'rate' && this.clock.settled && Math.abs(this.clock.error) <= this.clock.deadband / 2
					? settledSyncSpacingMs
					: minSyncSpacingMs;
			if (now() - this.sentAt < spacing) return;
			this.inFlight = true;
			this.sentAt = now();
			this.send('sync');
		}, minSyncSpacingMs);
	}

	stop() {
		clearInterval(this.syncTimer);
		try { this.socket.close(); } catch { /* already gone */ }
	}
}

/** `<seconds> [<serverUtcMs>]`. The stamp is `NaN` when the server did not send
 *  one, which is exactly what `stalenessOf` and `sample` treat as "no stamp". */
const timed = argument => {
	const [seconds, stamp] = argument.trim().split(/\s+/);
	return { seconds: Number(seconds), stamp: Number(stamp) };
};

const profiles = {
	LAN: { ping: 10, jitter: 2, driftPpm: 30, buffer: 120 },
	Home: { ping: 60, jitter: 8, driftPpm: -50, buffer: 400 },
	Mobile: { ping: 180, jitter: 40, driftPpm: 120, buffer: 1500 },
	Sat: { ping: 600, jitter: 90, driftPpm: -400, buffer: 2500 },
};

const listeners = Object.entries(profiles).map(([name, p]) => new Listener(name, p));
const [lan, home, mobile, sat] = listeners;

const room = (await (await fetch(`${API}/Audio/Multiplayer/CreateRoom`, { method: 'POST' })).json())
	.roomID;
const tracks = await (await fetch(`${API}/Audio/RandomResults?count=2`)).json();
console.log(`MODE=${MODE} SEED=${SEED} room ${room}`);

for (const listener of [lan, home, mobile]) await listener.connect(room);
await wait(400);
for (const listener of [lan, home, mobile]) listener.startSync();
for (const track of tracks) { lan.send(`add ${track.id}`); await wait(600); }

const samples = [];
const marks = [];
const startedAt = now();
const sampler = setInterval(() => {
	for (const listener of listeners) listener.tick();
	const live = listeners.filter(l => l.socket && l.playing);
	if (live.length < 2) return;

	const positions = live.map(l => l.position);
	const sorted = positions.slice().sort((a, b) => a - b);
	const median = sorted[sorted.length >> 1];
	for (const listener of live) listener.offsets.push(Math.abs(listener.position - median));
	samples.push(Math.max(...positions) - Math.min(...positions));
}, 50);

const phase = async (label, ms, act) => {
	if (act) await act();
	await wait(ms);
	marks.push([label, samples.length, now()]);
};

await phase('three clients, steady', 25_000);
await phase('Sat joins late (600 ms)', 25_000, async () => {
	await sat.connect(room);
	sat.startSync();
});
await phase('after skipto track 2', 25_000, () => home.send('skipto 1'));
await phase('after pause and resume', 20_000, async () => {
	mobile.send('playpause');
	await wait(3000);
	mobile.send('playpause');
});

clearInterval(sampler);
for (const listener of listeners) listener.stop();

/** How often the loop asked for exactly 1.0 — the rate the ear never notices. */
const still = rates =>
  rates.length ? ((rates.filter(r => r === 1).length / rates.length) * 100).toFixed(0) : '-';
/** Mean change between consecutive commanded rates, in parts per million. The
 *  complaint was that this never settles, so it is the number to watch. */
const motion = rates => {
  if (rates.length < 2) return '-';
  let sum = 0;
  for (let i = 1; i < rates.length; i++) sum += Math.abs(rates[i] - rates[i - 1]);
  return `${((sum / (rates.length - 1)) * 1e6).toFixed(0)}ppm`;
};

/** Commanded-rate changes per minute. This is the artifact metric: what the ear
 *  catches is the resampling ratio *moving*, not the value it sits at — 200 ppm is
 *  a third of a cent, and inaudible, but a ratio that never holds still is not.
 *  The requirement is a handful per song, so a song is the unit to think in. */
const churn = minutes => rates => {
	if (rates.length < 2) return '-';
	let changes = 0;
	for (let i = 1; i < rates.length; i++) if (rates[i] !== rates[i - 1]) changes++;
	return `${(changes / minutes).toFixed(1)}/min`;
};

const summarise = values => {
	const sorted = values.slice().sort((a, b) => a - b);
	if (sorted.length === 0) return 'no data';
	const at = q => (sorted[Math.floor(sorted.length * q)] * 1000).toFixed(0).padStart(4);
	return `n=${String(sorted.length).padStart(4)}  p50 ${at(0.5)}ms  p90 ${at(0.9)}ms  max ${(sorted.at(-1) * 1000).toFixed(0)}ms`;
};

console.log(`\nspread between listeners            ${summarise(samples)}`);
let from = 0;
for (const [label, to] of marks) {
	console.log(`  ${label.padEnd(32)} ${summarise(samples.slice(from, to))}`);
	from = to;
}

const changesPerMinute = churn((now() - startedAt) / 60_000);

/** Churn inside one phase — the steady one is the honest stand-in for a song,
 *  since the scripted session packs a join, a track change and a pause into 95
 *  seconds and a song contains none of that after its first moment. */
const phaseChurn = (listener, from, to) => {
	const rates = listener.rates.filter((_, i) => listener.rateTimes[i] >= from && listener.rateTimes[i] < to);
	return churn((to - from) / 60_000)(rates);
};

console.log('\nrate churn by phase (a song looks like the steady phase):');
{
	let from = startedAt;
	for (const [label, , until] of marks) {
		const per = listeners
			.map(l => `${l.name} ${phaseChurn(l, from, until)}`.padEnd(18))
			.join(' ');
		console.log(`  ${label.padEnd(30)} ${per}`);
		from = until;
	}
}

console.log('\nper listener:');
for (const listener of listeners) {
	const rtt = listener.rtts.slice().sort((a, b) => a - b);
	const drift = listener.clock.drift;
	console.log(
		`  ${listener.name.padEnd(7)} rtt min ${String(rtt[0]?.toFixed(0)).padStart(4)}ms` +
			` | off median ${summarise(listener.offsets).slice(10)}` +
			` | rate ${listener.rate.toFixed(5)} still ${still(listener.rates)}%` +
			` move ${motion(listener.rates)} churn ${changesPerMinute(listener.rates)}` +
			` | hard seeks ${listener.seeks}` +
			` | drift ${drift === null ? 'unknown' : `${(drift * 1e6).toFixed(0)}ppm`} (real ${listener.driftPpm})`,
	);
}
