import { audioWsUrl } from '$lib/discord';
import { createRoom, type Room } from '$requests/rooms';
import session from './session.svelte';

/**
 * The lobby feed. The server pushes the whole room array on connect and again on
 * every create and rename — there is no delta format and nothing to merge, and
 * it never reads from this socket, so nothing is ever sent on it.
 *
 * The room page keeps it open too: a rename confirms to the sender only, so
 * everybody else learns the room's title from here.
 */
class Rooms {
	list: Room[] = $state([]);
	connected: boolean = $state(false);

	private socket: WebSocket | null = null;
	private readers = 0;
	private attempts = 0;
	private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
	private announceReady: (() => void) | null = null;
	/** Resolves on the first list, so the Discord hand-off can look before it creates. */
	ready: Promise<void>;

	constructor() {
		this.ready = new Promise(resolve => (this.announceReady = resolve));
	}

	connect() {
		this.readers++;
		if (this.socket || this.reconnectTimer) return;
		this.open();
	}

	disconnect() {
		this.readers = Math.max(0, this.readers - 1);
		if (this.readers > 0) return;

		if (this.reconnectTimer !== null) clearTimeout(this.reconnectTimer);
		this.reconnectTimer = null;
		if (this.socket) {
			this.socket.onclose = null;
			this.socket.close();
			this.socket = null;
		}
		this.connected = false;
	}

	/**
	 * The voice channel you launched from is the room. `CreateRoom` takes no body,
	 * so the marker is written afterwards over the session socket.
	 */
	async findOrCreateForDiscord(marker: string, name: string): Promise<string> {
		// exact, not `includes`: snowflakes vary in length, so `discord:123` would
		// otherwise match the room marked `discord:1234`. Leading space is the
		// server's command-split quirk.
		const existing = () => this.list.find(room => room.description.trim() === marker);

		const found = existing();
		if (found) return found.roomID;

		// ponytail: jitter and re-check instead of a lock. Two clients starting
		// together at worst leave a spare empty room, never a split session — give
		// CreateRoom a body server-side and this goes away entirely.
		await new Promise(resolve => setTimeout(resolve, Math.random() * 400));
		const raced = existing();
		if (raced) return raced.roomID;

		const room = await createRoom();
		session.prime([`updateroom name ${name}`, `updateroom description ${marker}`]);
		return room.roomID;
	}

	private open() {
		this.reconnectTimer = null;
		const socket = new WebSocket(audioWsUrl('/Multiplayer/Rooms'));
		this.socket = socket;

		socket.onopen = () => {
			this.attempts = 0;
			this.connected = true;
		};
		socket.onmessage = event => {
			try {
				this.list = JSON.parse(String(event.data)) as Room[];
			} catch {
				return;
			}
			this.announceReady?.();
			this.announceReady = null;
		};
		socket.onclose = () => {
			this.socket = null;
			this.connected = false;
			if (this.readers === 0) return;
			this.attempts++;
			this.reconnectTimer = setTimeout(
				() => this.open(),
				Math.min(1000 * 2 ** (this.attempts - 1), 30_000),
			);
		};
	}
}

export default new Rooms();
