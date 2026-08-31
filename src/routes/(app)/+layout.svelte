<script lang="ts">
	// self-hosted so they survive the Discord activity's CSP (no external font hosts)
	import '@fontsource-variable/unbounded/wght.css';
	import '@fontsource-variable/golos-text/wght.css';
	import '@fontsource-variable/jetbrains-mono/wght.css';
	import '../../app.css';
	import { onMount } from 'svelte';
	import { initDiscord } from '$lib/discord';
	import Header from '$components/header/Header.svelte';
	import Player from '$components/player/Player.svelte';
	import Queue from '$components/queue/Queue.svelte';
	let { children } = $props();
	let showQueue = $state(false);

	// clears Discord's activity loading screen; a no-op in a normal browser tab
	onMount(initDiscord);
</script>

<div class="relative flex flex-col w-full h-svh overflow-hidden">
	<Header />
	<main
		class:queue-open={showQueue}
		class="relative m-2 mt-0 flex h-full min-h-0 flex-col rounded-lg bg-dark-0 transition-[margin]"
	>
		{@render children()}
	</main>
	{#if showQueue}
		<!-- a sheet on narrow screens, a dock that narrows the page from lg up -->
		<aside
			class="absolute inset-x-2 bottom-20 top-1/3 z-20 sm:inset-x-auto sm:right-2 sm:top-16 sm:w-[380px]"
		>
			<Queue />
		</aside>
	{/if}
	<Player bind:showQueue />
</div>
