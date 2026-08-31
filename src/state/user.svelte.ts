import { browser } from '$app/environment';

const storageKey = 'musicrain.username';

/**
 * The server takes a name once, in the query string that opens the session
 * socket, and never again for that connection. So the name has to exist before
 * the join, and changing it means reconnecting as somebody new.
 */
class Identity {
	/** `null` until the reader has chosen. An empty string is a real choice: it
	 *  joins without a name and the server calls you `Anonymous <id>`. */
	username: string | null = $state(null);
	avatarUrl: string | null = $state(null);
	source: 'local' | 'discord' = $state('local');

	get chosen() {
		return this.username !== null;
	}

	load() {
		if (!browser) return;
		const stored = localStorage.getItem(storageKey);
		if (stored !== null) this.username = stored;
	}

	/**
	 * Discord already knows who you are, so the name prompt is dead weight inside
	 * the activity. Deliberately not written to `localStorage`: the same browser
	 * profile may open the site outside Discord, and that visit keeps its own name.
	 */
	adopt(name: string, avatarUrl: string | null) {
		this.username = name;
		this.avatarUrl = avatarUrl;
		this.source = 'discord';
	}

	choose(name: string) {
		this.username = name.trim();
		if (browser) localStorage.setItem(storageKey, this.username);
	}
}

export default new Identity();
