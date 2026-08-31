import { DiscordSDK } from '@discord/embedded-app-sdk';
import type { SearchResult } from '$states/search.svelte';

/**
 * Discord serves an activity from `<client_id>.discordsays.com` and appends the
 * `frame_id` / `instance_id` query params the SDK needs. Either signal is enough.
 */
export const isDiscordActivity =
	typeof location !== 'undefined' &&
	(location.hostname.endsWith('.discordsays.com') ||
		new URLSearchParams(location.search).has('frame_id'));

/**
 * Inside the activity iframe every external host is blocked by Discord's CSP —
 * only the URL mappings configured in the Developer Portal are reachable, under
 * `/.proxy/<prefix>`. See the "Discord Activities" section of the README.
 */
export const audioApi = isDiscordActivity ? '/.proxy/api/Audio' : 'https://api.gergov.bg/Audio';

const YTIMG = '.ytimg.com';

/** Rewrites an absolute URL the API handed us onto the Discord proxy. */
function proxied(url: string | null): string | null {
	if (!url || !isDiscordActivity) return url;
	let parsed: URL;
	try {
		parsed = new URL(url, location.origin);
	} catch {
		return url;
	}
	const { hostname, pathname, search } = parsed;
	if (hostname === 'api.gergov.bg') return `/.proxy/api${pathname}${search}`;
	if (hostname.endsWith(YTIMG))
		return `/.proxy/ytimg/${hostname.slice(0, -YTIMG.length)}${pathname}${search}`;
	// ponytail: unmapped host stays as-is — the iframe blocks it and the <img>
	// onerror handlers already fall back to /empty.png. Add a mapping if one shows up.
	return url;
}

export function proxyThumbnails<T extends SearchResult>(results: T[]): T[] {
	if (!isDiscordActivity) return results;
	return results.map((r) => ({ ...r, thumbnailUrl: proxied(r.thumbnailUrl) }));
}

export let sdk: DiscordSDK | null = null;

/**
 * Handshakes with the Discord client. Without `ready()` the activity sits on
 * Discord's loading screen forever. Outside Discord this is a no-op.
 *
 * After this resolves, `sdk.channelId` / `sdk.guildId` / `sdk.instanceId`
 * identify the room the activity was launched in.
 */
export async function initDiscord() {
	if (!isDiscordActivity || sdk) return;

	const clientId = import.meta.env.VITE_DISCORD_CLIENT_ID;
	if (!clientId) {
		console.error('VITE_DISCORD_CLIENT_ID is unset — the activity cannot start.');
		return;
	}

	sdk = new DiscordSDK(clientId);
	await sdk.ready();
}
