import { login, logout, register, type Session } from '$requests/accounts';
import user from './user.svelte';

const storageKey = 'musicrain.token';

/** Absent while prerendering, and in a browser that refuses storage. Either way: no session. */
const storage = () => (typeof localStorage === 'undefined' ? null : localStorage);

/**
 * Who you are signed in as, and the bearer token that says so.
 *
 * Deliberately separate from `user.svelte` — that one is a name for a room socket,
 * chosen per browser profile, and an account is a thing on a server. Signing in
 * offers the account's name to the room identity; it does not replace it.
 */
class Account {
	username: string | null = $state(null);
	token: string | null = $state(null);
	/** Set by the panel while a request is in flight, so the button can say so. */
	busy = $state(false);
	error = $state('');

	get signedIn() {
		return this.token !== null;
	}

	load() {
		const stored = storage()?.getItem(storageKey);
		if (!stored) return;

		try {
			const session = JSON.parse(stored) as Session;
			// a token past its expiry is not a token — sign the panel out before it
			// makes a request that can only fail
			if (Date.parse(session.expiresUtc) <= Date.now()) return this.forget();

			this.username = session.username;
			this.token = session.token;
		} catch {
			this.forget();
		}
	}

	signUp(username: string, password: string) {
		return this.attempt(() => register(username, password));
	}

	signIn(username: string, password: string) {
		return this.attempt(() => login(username, password));
	}

	/** Best effort: the token is dropped here whether or not Dom heard about it. */
	async signOut() {
		const token = this.token;
		this.forget();
		if (token) await logout(token).catch(() => undefined);
	}

	private async attempt(request: () => Promise<Session>) {
		this.busy = true;
		this.error = '';
		try {
			this.keep(await request());
			return true;
		} catch (error) {
			this.error = error instanceof Error ? error.message : 'Could not reach the audio service.';
			return false;
		} finally {
			this.busy = false;
		}
	}

	private keep(session: Session) {
		this.username = session.username;
		this.token = session.token;
		storage()?.setItem(storageKey, JSON.stringify(session));

		// The room identity is a separate thing, but nobody wants to be `kris` here and
		// `Anonymous 4` in a room. Discord's own name still wins inside the activity.
		if (user.source !== 'discord') user.choose(session.username);
	}

	private forget() {
		this.username = null;
		this.token = null;
		storage()?.removeItem(storageKey);
	}
}

export default new Account();
