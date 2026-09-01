import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { minSyncSpacingMs, proportionalGain, syncDeadbandSeconds } from '$lib/syncClock';
import type Session from './session.svelte';
import type Queue from './queue.svelte';
import type Audio from './audio.svelte';

class FakeSocket {
	static last: FakeSocket;
	static readonly OPEN = 1;

	readyState = FakeSocket.OPEN;
	sent: string[] = [];
	onopen: (() => void) | null = null;
	onmessage: ((event: { data: string }) => void) | null = null;
	onclose: (() => void) | null = null;

	constructor(readonly url: string) {
		FakeSocket.last = this;
	}

	send(command: string) {
		this.sent.push(command);
	}

	close() {
		this.readyState = 3;
	}
}

let session: typeof Session;
let queue: typeof Queue;
let audio: typeof Audio;

/** Everything the server can say arrives as one text frame. */
function receive(frame: string) {
	FakeSocket.last.onmessage?.({ data: frame });
}

const item = (over: Record<string, unknown> = {}) => ({
	id: 'audio://a',
	name: 'Stone Cold Crazy',
	artist: 'Queen',
	album: null,
	duration: '00:02:12.5000000',
	thumbnailUrl: null,
	originalTitle: null,
	originalArtist: null,
	...over,
});

beforeEach(async () => {
	vi.resetModules();
	vi.stubGlobal('WebSocket', FakeSocket);
	vi.stubGlobal('location', new URL('http://localhost:5173/room'));

	session = (await import('./session.svelte')).default;
	queue = (await import('./queue.svelte')).default;
	audio = (await import('./audio.svelte')).default;

	session.connect('0f0f4e0c', 'Kris G');
	FakeSocket.last.onopen?.();
	FakeSocket.last.sent.length = 0;
});

afterEach(() => {
	session.disconnect();
	vi.unstubAllGlobals();
});

describe('joining', () => {
	it('names you in the query string, encoded', () => {
		expect(FakeSocket.last.url).toBe(
			'wss://api.gergov.bg/Audio/Multiplayer/Join?room=0f0f4e0c&username=Kris%20G',
		);
	});

	it('treats a close with zero frames as a room that does not exist', () => {
		FakeSocket.last.onclose?.();

		expect(session.gone).toBe(true);
		expect(session.inRoom).toBe(false);
	});
});

describe('losing the connection', () => {
	/** Drops the socket and lets the backoff bring a new one up. */
	function reconnect() {
		FakeSocket.last.onclose?.();
		vi.advanceTimersByTime(30_000);
		FakeSocket.last.onopen?.();
	}

	beforeEach(() => {
		vi.useFakeTimers();
		// a frame has to land first, or a close reads as a room that never existed
		receive('room name Kitchen');
	});

	afterEach(() => {
		vi.useRealTimers();
	});

	it('drops commands issued while down instead of replaying them at the room', () => {
		FakeSocket.last.onclose?.();

		// everything a bored user can press while the strip says "reconnecting"
		session.send('playpause');
		queue.nextTrack();
		session.reportEnded();

		vi.advanceTimersByTime(30_000);
		FakeSocket.last.onopen?.();

		expect(FakeSocket.last.sent).not.toContain('playpause');
		expect(FakeSocket.last.sent).not.toContain('next');
		expect(FakeSocket.last.sent).not.toContain('end');
	});

	it('keeps its place in the track across a reconnect', () => {
		receive(`queue ${JSON.stringify([item()])}`);
		receive('current 0');
		session.reportLoaded();
		receive('playing True');
		audio.currentSeconds = 45;

		reconnect();
		receive(`queue ${JSON.stringify([item()])}`);
		receive('current 0');

		expect(audio.currentSeconds).toBe(45);
	});

	it('does not answer the loading barrier again for a track it already answered', () => {
		receive(`queue ${JSON.stringify([item()])}`);
		receive('current 0');
		session.reportLoaded();
		receive('playing True');

		reconnect();
		// `Joined` replays the room's state; the server arms the barrier only on a
		// real track change, so an answer here is an extra vote in a raw tally
		receive(`queue ${JSON.stringify([item()])}`);
		receive('current 0');
		session.reportLoaded();

		expect(session.awaitingLoad).toBe(false);
		expect(FakeSocket.last.sent).not.toContain('loaded');
	});

	it('still answers the barrier for a track the room moved to while it was away', () => {
		receive(`queue ${JSON.stringify([item(), item({ id: 'audio://b' })])}`);
		receive('current 0');
		session.reportLoaded();

		reconnect();
		receive(`queue ${JSON.stringify([item(), item({ id: 'audio://b' })])}`);
		receive('current 1');

		expect(session.awaitingLoad).toBe(true);
		session.reportLoaded();
		expect(FakeSocket.last.sent).toContain('loaded');
	});
});

describe('the loading barrier', () => {
	it('holds the barrier until the player can really play, not on the frame', () => {
		receive(`queue ${JSON.stringify([item()])}`);
		receive('current 0');

		// a `current` frame only means the URL changed; nothing is buffered yet
		expect(FakeSocket.last.sent).not.toContain('loaded');
		expect(session.awaitingLoad).toBe(true);
		expect(session.status).toBe('holding');

		session.reportLoaded();
		expect(FakeSocket.last.sent).toContain('loaded');
		expect(session.awaitingLoad).toBe(false);
	});

	it('reports loaded exactly once per track, even if current repeats', () => {
		receive(`queue ${JSON.stringify([item(), item({ id: 'audio://b' })])}`);
		receive('current 1');
		receive('current 1');
		session.reportLoaded();
		session.reportLoaded();

		expect(FakeSocket.last.sent.filter(command => command === 'loaded')).toHaveLength(1);
		expect(queue.currentIndex).toBe(1);
		expect(audio.paused).toBe(true);
		expect(session.status).toBe('holding');
	});

	it('answers late rather than never when the track never buffers', () => {
		vi.useFakeTimers();
		try {
			receive(`queue ${JSON.stringify([item()])}`);
			receive('current 0');
			expect(FakeSocket.last.sent).not.toContain('loaded');

			vi.advanceTimersByTime(15_000);
			expect(FakeSocket.last.sent.filter(command => command === 'loaded')).toHaveLength(1);
		} finally {
			vi.useRealTimers();
		}
	});

	it('still reports loaded immediately when current points past the end', () => {
		receive('queue []');
		receive('current 0');

		// nothing to buffer, so nothing to wait for
		expect(FakeSocket.last.sent).toContain('loaded');
		expect(session.awaitingLoad).toBe(false);
	});

	it('does not restart the track it is already playing when the room replays it', () => {
		receive(`queue ${JSON.stringify([item()])}`);
		receive('current 0');
		session.reportLoaded();
		receive('playing True');
		audio.currentSeconds = 60;

		// rejoining replays the room's whole state, `current` included
		receive(`queue ${JSON.stringify([item()])}`);
		receive('current 0');

		expect(audio.currentSeconds).toBe(60);
		expect(audio.paused).toBe(false);
	});

	it('starts a track added to an idle room from the top', () => {
		// joining an empty room answers a `current` frame with nothing to load
		receive('queue []');
		receive('current 0');
		FakeSocket.last.sent.length = 0;
		// whatever the room's clock had wandered to while nothing was playing
		audio.currentSeconds = 14;

		receive(`queue ${JSON.stringify([item()])}`);

		expect(audio.currentSeconds).toBe(0);
		expect(audio.paused).toBe(true);
		expect(session.awaitingLoad).toBe(true);
		expect(session.status).toBe('holding');
	});

	it('releases on playing True', () => {
		receive(`queue ${JSON.stringify([item()])}`);
		receive('current 0');
		receive('playing True');

		expect(audio.paused).toBe(false);
		expect(session.status).toBe('synced');
	});
});

describe('queue frames', () => {
	it('prefers the untransliterated title and fills in the nulls', () => {
		receive(
			`queue ${JSON.stringify([
				item({ name: null, artist: null }),
				item({
					id: 'audio://b',
					originalTitle: 'Бизнесмен',
					originalArtist: 'Тоника',
				}),
			])}`,
		);

		expect(queue.items[0].name).toBe('Unknown title');
		expect(queue.items[0].artist).toBe('Unknown artist');
		expect(queue.items[1].name).toBe('Бизнесмен');
		expect(queue.items[1].artist).toBe('Тоника');
	});
});

describe('parsing', () => {
	it('splits chat on the first separator only, and trims the leaked space', () => {
		receive('chat Alice %%  hi %% there');

		expect(session.chat.at(-1)).toMatchObject({
			username: 'Alice',
			text: 'hi %% there',
		});
	});

	it('reads room name and description as two-token prefixes', () => {
		receive('room name  Friday mix');
		receive('room description  late night');

		expect(session.name).toBe('Friday mix');
		expect(session.description).toBe('late night');
	});

	it('builds the roster from system notices', () => {
		receive("chat System %%  User 'ana' joined the session.");
		receive("chat System %%  User 'bo' joined the session.");
		receive("chat System %%  User 'ana' left the session.");

		expect(session.roster).toEqual(['bo']);
	});
});

describe('the shared clock', () => {
	/** Runs the clock up to the moment the next `sync` actually leaves — the
	 *  interval's own phase would otherwise be added to every round trip — then
	 *  waits one out and answers it. */
	function roundTrip(reported: number, rttMs = 200) {
		const before = FakeSocket.last.sent.length;
		while (FakeSocket.last.sent.length === before) vi.advanceTimersByTime(1);
		vi.advanceTimersByTime(rttMs);
		receive(`sync ${reported}`);
	}

	beforeEach(() => {
		// the loop is timed off `performance.now()`, so that has to move with the
		// timers or every trip reads as one enormous round trip
		vi.useFakeTimers({
			toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'performance'],
		});
		session.connect('0f0f4e0c', 'Kris G');
		FakeSocket.last.onopen?.();

		receive(`queue ${JSON.stringify([item()])}`);
		receive('current 0');
		receive('playing True');
		FakeSocket.last.sent.length = 0;
	});

	afterEach(() => {
		vi.useRealTimers();
	});

	it('keeps one sync in flight, since the replies carry no id', () => {
		vi.advanceTimersByTime(minSyncSpacingMs * 4);
		expect(FakeSocket.last.sent.filter(command => command === 'sync')).toHaveLength(1);

		receive('sync 0');
		vi.advanceTimersByTime(minSyncSpacingMs);
		expect(FakeSocket.last.sent.filter(command => command === 'sync')).toHaveLength(2);
	});

	it('does not wedge shut when a reply never comes', () => {
		vi.advanceTimersByTime(minSyncSpacingMs);
		expect(FakeSocket.last.sent.filter(command => command === 'sync')).toHaveLength(1);

		// nothing answers it: the loop has to give up on that trip and ask again
		vi.advanceTimersByTime(6_000);
		expect(FakeSocket.last.sent.filter(command => command === 'sync').length).toBeGreaterThan(1);
	});

	it('snaps once at the top of a track, crediting the trip in flight', () => {
		audio.currentSeconds = 10;
		// the room said 10.3 a hundred milliseconds ago, so it is really at 10.4
		roundTrip(10.3);

		expect(audio.currentSeconds).toBeCloseTo(10.4);
		expect(audio.rate).toBe(1);
	});

	it('steers with the rate once the track has had its opening jump', () => {
		audio.currentSeconds = 10;
		roundTrip(10.3);
		const settled = audio.currentSeconds;

		// 100 ms out: well inside what the rate can close
		roundTrip(settled + 0.05, 100);

		expect(audio.currentSeconds).toBeCloseTo(settled);
		expect(audio.rate).toBeCloseTo(1 + proportionalGain * (0.1 - syncDeadbandSeconds));
	});

	it('never asks for more than the rate budget allows', () => {
		roundTrip(0);
		roundTrip(0.7, 200);

		expect(audio.rate).toBeLessThanOrEqual(1.02);
		expect(audio.rate).toBeGreaterThanOrEqual(0.98);
	});

	it('leads a seek frame by the trip it spent arriving', () => {
		roundTrip(0, 200);
		receive('seek 30');

		expect(audio.currentSeconds).toBeCloseTo(30.1);
	});

	it('hands the track change a clock that knows the link but not the position', () => {
		roundTrip(0, 200);
		audio.rate = 1.01;

		receive('current 1');

		expect(audio.rate).toBe(1);
		expect(session.offsetMs).toBe(0);
		// the link did not change, so the seek lead has to survive
		receive('seek 5');
		expect(audio.currentSeconds).toBeCloseTo(5.1);
	});

	it('holds the rate at exactly 1 while the room is together enough', () => {
		roundTrip(0);
		roundTrip(audio.currentSeconds + 0.005, 20);

		// 15 ms out, and the loop must not be reaching for it
		expect(session.offsetMs).toBe(15);
		expect(audio.rate).toBe(1);
		expect(session.status).toBe('synced');
	});

	it('says catching up while it is, and synced once it is not', () => {
		audio.currentSeconds = 10;
		roundTrip(10.3);
		roundTrip(audio.currentSeconds + 0.2, 400);
		expect(session.status).toBe('catching up');

		// a quicker trip, so this is the sample the estimator ends up trusting
		roundTrip(audio.currentSeconds + 0.005, 20);
		expect(session.status).toBe('synced');
	});

	it('withholds a drift reading until the history can support one', () => {
		roundTrip(0);
		expect(session.driftPpm).toBeNull();
	});

	it('mirrors the readings out of the clock, which is not reactive itself', () => {
		audio.currentSeconds = 10;
		roundTrip(10.3, 180);

		// 300 ms reported plus the 90 ms the reply spent arriving
		expect(session.offsetMs).toBe(390);
		expect(session.pingMs).toBe(180);
		expect(session.driftPpm).toBeNull();
	});
});

describe('queue verbs while connected', () => {
	it('command the room instead of mutating the list', () => {
		receive(`queue ${JSON.stringify([item(), item({ id: 'audio://b' })])}`);
		const before = [...queue.items];
		FakeSocket.last.sent.length = 0;

		queue.removeIndex(0);
		queue.playIndex(1);
		queue.nextTrack();
		queue.shuffle();

		expect(FakeSocket.last.sent).toEqual(['remove 0', 'skipto 1', 'next', 'shuffle']);
		expect(queue.items).toEqual(before);
	});
});
