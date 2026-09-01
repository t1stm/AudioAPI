import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
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

describe('drift', () => {
	it('corrects only past the tolerance', () => {
		audio.currentSeconds = 10;
		receive('sync 10.3');
		expect(audio.currentSeconds).toBe(10);

		receive('sync 12.3456789');
		expect(audio.currentSeconds).toBeCloseTo(12.3456789);
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
