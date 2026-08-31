import type { SearchResult } from '$states/search.svelte';
import { audioWsUrl, proxyThumbnails } from '$lib/discord';
import queue from './queue.svelte';
import current from './current.svelte';
import audio from './audio.svelte';

/** What the room's `queue` frame actually carries: the raw platform result, not
 *  the search shape. No `contentUrl`, and most fields are nullable. */
type RoomQueueItem = {
	id: string;
	name: string | null;
	artist: string | null;
	album: string | null;
	duration: string;
	thumbnailUrl: string | null;
	originalTitle: string | null;
	originalArtist: string | null;
};

export type ChatLine = {
	id: number;
	username: string;
	text: string;
	system: boolean;
	at: string;
};

/** The strip speaks in the wire's own words: a shared clock that drifts reads as
 *  a bug when it is unlabelled and as a state when it is named. */
export type SessionStatus =
	| 'offline'
	| 'connecting'
	| 'holding'
	| 'synced'
	| 'catching up'
	| 'paused'
	| 'stopped'
	| 'reconnecting';

const maximumChatLines = 200;
// `sync` replies to the sender only and does not touch anyone else's playback,
// so a tight interval costs one small frame per second and buys a much shorter
// window in which a client can sit audibly behind the room.
const syncIntervalMs = 1_000;
const driftToleranceSeconds = 0.5;

function toSearchResult(item: RoomQueueItem): SearchResult {
	return {
		id: item.id,
		// search results are normalised server-side, queue items are not
		name: item.originalTitle ?? item.name ?? 'Unknown title',
		artist: item.originalArtist ?? item.artist ?? 'Unknown artist',
		album: item.album ?? undefined,
		duration: item.duration,
		thumbnailUrl: item.thumbnailUrl,
	};
}

let nextLineId = 0;

class Session {
	roomId: string | null = $state(null);
	joinedAs: string = $state('');
	name: string = $state('');
	description: string = $state('');
	status: SessionStatus = $state('offline');
	chat: ChatLine[] = $state([]);
	/** Presence exists only as system chat notices, so this misses everyone who
	 *  arrived before you and resets on reconnect. Label it honestly. */
	roster: string[] = $state([]);
	unread: number = $state(0);
	chatOpen: boolean = $state(false);
	/** A well-formed but unknown room GUID is accepted, then closed with no
	 *  frames. Zero frames received is the only "room does not exist" signal. */
	gone: boolean = $state(false);

	private socket: WebSocket | null = null;
	private pending: string[] = [];
	private attempts = 0;
	private frames = 0;
	private loadedFor: number | null = null;
	private endedFor: number | null = null;
	private currentTrackId = '';
	private awaitingBarrier = false;
	private closing = false;
	private syncTimer: ReturnType<typeof setInterval> | null = null;
	private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
	private driftTimer: ReturnType<typeof setTimeout> | null = null;

	get inRoom() {
		return this.roomId !== null;
	}

	/** Commands to run as soon as the next connection opens — how a freshly
	 *  created room gets its name, since `CreateRoom` takes no body. */
	prime(commands: string[]) {
		this.pending.push(...commands);
	}

	connect(roomId: string, username: string) {
		this.teardown();
		this.closing = false;
		this.attempts = 0;
		this.roomId = roomId;
		this.joinedAs = username;
		this.gone = false;
		this.name = '';
		this.description = '';
		this.chat = [];
		this.roster = [];
		this.unread = 0;
		this.currentTrackId = '';
		queue.items = [];
		queue.currentIndex = 0;
		current.clear();
		audio.paused = true;
		audio.currentSeconds = 0;
		// set before the socket opens so a click in the meantime queues a command
		// instead of quietly mutating a queue the server owns
		queue.remote = command => this.send(command);
		this.open();
	}

	disconnect() {
		this.closing = true;
		this.teardown();
		this.pending = [];
		this.roomId = null;
		this.joinedAs = '';
		this.status = 'offline';
		this.awaitingBarrier = false;
		queue.remote = null;
		queue.clear();
	}

	send(command: string) {
		if (this.socket?.readyState === WebSocket.OPEN) this.socket.send(command);
		else this.pending.push(command);
	}

	/** Sent exactly once per track. Both barriers count messages, not distinct
	 *  users, so a second `end` releases the barrier early for everybody. */
	reportEnded() {
		if (this.endedFor === queue.currentIndex) return;
		this.endedFor = queue.currentIndex;
		this.send('end');
	}

	rename(name: string) {
		const trimmed = name.trim();
		if (!trimmed) return;
		this.send(`updateroom name ${trimmed}`);
	}

	private open() {
		this.status = this.attempts === 0 ? 'connecting' : 'reconnecting';
		this.frames = 0;
		this.loadedFor = null;
		this.endedFor = null;

		let query = `room=${this.roomId}`;
		if (this.joinedAs) query += `&username=${encodeURIComponent(this.joinedAs)}`;

		const socket = new WebSocket(audioWsUrl(`/Multiplayer/Join?${query}`));
		this.socket = socket;

		socket.onopen = () => {
			this.attempts = 0;
			for (const command of this.pending.splice(0)) socket.send(command);
			this.syncTimer = setInterval(() => this.send('sync'), syncIntervalMs);
		};
		socket.onmessage = event => {
			this.frames++;
			this.receive(String(event.data));
		};
		socket.onclose = () => this.closed();
	}

	private closed() {
		this.clearTimer('syncTimer');
		this.socket = null;
		if (this.closing || this.roomId === null) return;

		if (this.frames === 0) {
			this.gone = true;
			this.roomId = null;
			this.status = 'offline';
			queue.remote = null;
			return;
		}

		this.status = 'reconnecting';
		this.attempts++;
		this.reconnectTimer = setTimeout(
			() => this.open(),
			Math.min(1000 * 2 ** (this.attempts - 1), 30_000),
		);
	}

	private receive(raw: string) {
		const space = raw.indexOf(' ');
		const command = space === -1 ? raw : raw.slice(0, space);
		const argument = space === -1 ? '' : raw.slice(space + 1);

		switch (command) {
			case 'queue':
				this.setQueue(argument);
				break;
			case 'current':
				this.setCurrent(Number(argument));
				break;
			case 'playing':
				audio.paused = argument.trim() !== 'True';
				if (!audio.paused) {
					this.awaitingBarrier = false;
					this.status = 'synced';
				} else this.status = this.awaitingBarrier ? 'holding' : 'paused';
				break;
			case 'seek':
				audio.currentSeconds = Number(argument);
				break;
			case 'sync':
				this.correctDrift(Number(argument));
				break;
			case 'stop':
				audio.paused = true;
				this.awaitingBarrier = false;
				this.status = 'stopped';
				break;
			case 'chat':
				this.addChat(argument);
				break;
			case 'room':
				this.setRoomField(argument);
				break;
		}
	}

	private setQueue(json: string) {
		let items: RoomQueueItem[];
		try {
			items = JSON.parse(json);
		} catch {
			return;
		}
		queue.items = proxyThumbnails(items.map(toSearchResult));

		// `remove` shifts `current` implicitly and broadcasts only a queue, so the
		// playing item has to be re-derived after every list
		const now = queue.items[queue.currentIndex];
		if (now && now.id !== this.currentTrackId) {
			this.currentTrackId = now.id;
			current.set(now);
		}
	}

	private setCurrent(index: number) {
		if (!Number.isFinite(index)) return;
		queue.currentIndex = index;
		audio.paused = true;
		audio.currentSeconds = 0;
		this.awaitingBarrier = true;
		this.status = 'holding';

		// `next` can move current one past the end, which means nothing is playing
		const now = queue.items[index];
		if (now) {
			this.currentTrackId = now.id;
			current.set(now);
		} else {
			this.currentTrackId = '';
			current.clear();
		}

		// exactly once per track, and even with nothing to load — the server counts
		// this against every member before it starts the clock again
		if (this.loadedFor === index) return;
		this.loadedFor = index;
		this.endedFor = null;
		this.send('loaded');
	}

	private correctDrift(reported: number) {
		if (!Number.isFinite(reported)) return;
		if (Math.abs(audio.currentSeconds - reported) <= driftToleranceSeconds) return;

		audio.currentSeconds = reported;
		this.status = 'catching up';
		this.clearTimer('driftTimer');
		this.driftTimer = setTimeout(() => {
			if (this.status === 'catching up') this.status = audio.paused ? 'paused' : 'synced';
		}, 1200);
	}

	private addChat(argument: string) {
		// split on the first ` %% ` only: the username is an unvalidated query
		// parameter and the text may contain another separator
		const separator = argument.indexOf(' %% ');
		const username = (separator === -1 ? argument : argument.slice(0, separator)).trim();
		const text = (separator === -1 ? '' : argument.slice(separator + 4)).trim();
		const system = username === 'System';

		if (system) this.updateRoster(text);

		const line: ChatLine = {
			id: nextLineId++,
			username,
			text,
			system,
			at: new Date().toLocaleTimeString([], {
				hour: '2-digit',
				minute: '2-digit',
			}),
		};
		this.chat = [...this.chat, line].slice(-maximumChatLines);
		if (!this.chatOpen) this.unread++;
	}

	private updateRoster(text: string) {
		const notice = /^User '(.*)' (joined|left)/.exec(text);
		if (!notice) return;
		const who = notice[1].trim();

		if (notice[2] === 'joined') {
			if (!this.roster.includes(who)) this.roster = [...this.roster, who];
			return;
		}
		this.roster = this.roster.filter(name => name !== who);
	}

	private setRoomField(argument: string) {
		const space = argument.indexOf(' ');
		if (space === -1) return;
		const field = argument.slice(0, space);
		const value = argument.slice(space + 1).trim();

		if (field === 'name') this.name = value;
		else if (field === 'description') this.description = value;
	}

	private clearTimer(which: 'syncTimer' | 'reconnectTimer' | 'driftTimer') {
		const timer = this[which];
		if (timer === null) return;
		clearTimeout(timer as ReturnType<typeof setTimeout>);
		clearInterval(timer as ReturnType<typeof setInterval>);
		this[which] = null;
	}

	/** Drops the socket and its timers without touching primed commands. */
	private teardown() {
		this.clearTimer('syncTimer');
		this.clearTimer('reconnectTimer');
		this.clearTimer('driftTimer');
		if (!this.socket) return;
		this.socket.onclose = null;
		this.socket.close();
		this.socket = null;
	}
}

export default new Session();
