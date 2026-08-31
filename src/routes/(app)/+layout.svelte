<script lang="ts">
	// self-hosted so they survive the Discord activity's CSP (no external font hosts)
	import '@fontsource-variable/unbounded/wght.css';
	import '@fontsource-variable/golos-text/wght.css';
	import '@fontsource-variable/jetbrains-mono/wght.css';
	import '../../app.css';
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import { resolve } from '$app/paths';
	import { discordIds, discordUser, initDiscord } from '$lib/discord';
	import Header from '$components/header/Header.svelte';
	import Player from '$components/player/Player.svelte';
	import Queue from '$components/queue/Queue.svelte';
	import Chat from '$components/chat/Chat.svelte';
	import SessionStrip from '$components/session/SessionStrip.svelte';
	import queue from '$states/queue.svelte';
	import rooms from '$states/rooms.svelte';
	import session from '$states/session.svelte';
	import user from '$states/user.svelte';

	let { children } = $props();
	let dock = $state<'queue' | 'chat' | null>(null);

	$effect(() => {
		session.chatOpen = dock === 'chat';
		if (dock === 'chat') session.unread = 0;
	});

	// a rename confirms to the sender only, so the room's title reaches everybody
	// else through the lobby feed — keep it open for the whole session, not just
	// while the room route is mounted
	$effect(() => {
		if (!session.inRoom) return;
		rooms.connect();
		return () => rooms.disconnect();
	});

	onMount(async () => {
		user.load();
		// clears Discord's activity loading screen; a no-op in a normal browser tab
		await initDiscord();
		// skips the "pick a name" gate on the room page and fills the header avatar
		if (discordUser) user.adopt(discordUser.name, discordUser.avatarUrl);

		const ids = discordIds();
		if (!ids || page.url.pathname.startsWith('/room')) return;

		// the voice channel you launched from is the room — never make anyone pick
		// it out of a list
		rooms.connect();
		await Promise.race([rooms.ready, new Promise((done) => setTimeout(done, 4000))]);
		try {
			// The channel is the room, not the launch: Discord mints a fresh
			// `instanceId` every time the activity is started, so keying on it made a
			// new room per launch instead of rejoining the channel's own.
			// ponytail: the channel's real name needs sdk.commands.authenticate(),
			// which needs a token endpoint the API does not have yet. Rename in-session.
			const key = ids.channelId ?? ids.instanceId;
			const roomId = await rooms.findOrCreateForDiscord(`discord:${key}`, 'Discord activity');
			await goto(`${resolve('/room')}?id=${roomId}`);
		} finally {
			rooms.disconnect();
		}
	});
</script>

<div class="relative flex flex-col w-full h-svh overflow-hidden">
	<Header />
	{#if session.inRoom}
		<SessionStrip />
	{/if}
	<main
		class:queue-open={dock !== null}
		class="relative m-2 mt-0 flex h-full min-h-0 flex-col rounded-lg bg-dark-0 transition-[margin]"
	>
		{@render children()}
	</main>
	{#if dock}
		<!-- a sheet on narrow screens, a dock that narrows the page from lg up. One
		     surface, two tabs — chat and queue never compete for the right edge. -->
		<aside
			class="absolute inset-x-2 bottom-20 top-1/3 z-20 flex flex-col overflow-hidden rounded-panel border border-haze bg-surface-100/95 backdrop-blur-xl sm:inset-x-auto sm:right-2 sm:top-16 sm:w-[380px]"
		>
			<div class="flex shrink-0 border-b border-haze">
				{#each [{ id: 'queue' as const, label: `Queue · ${queue.items.length}` }, { id: 'chat' as const, label: 'Chat' }] as tab (tab.id)}
					<button
						type="button"
						class="flex-1 px-3 py-2 text-xs font-semibold text-fog hover:text-chalk focus-visible:outline-2 focus-visible:outline-primary-200"
						class:bg-surface-200={dock === tab.id}
						class:text-chalk={dock === tab.id}
						onclick={() => (dock = tab.id)}
					>
						{tab.label}
					</button>
				{/each}
			</div>
			<div class="flex min-h-0 flex-1 flex-col px-3 pb-3 text-chalk">
				{#if dock === 'queue'}
					<Queue />
				{:else}
					<Chat />
				{/if}
			</div>
		</aside>
	{/if}
	<Player bind:dock />
</div>
