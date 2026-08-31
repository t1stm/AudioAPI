<script lang="ts">
	import '../../app.css';
	import Header from '$components/header/Header.svelte';
	import Player from '$components/player/Player.svelte';
	import Queue from '$components/queue/Queue.svelte';
	let { children } = $props();
	let showQueue = $state(false);
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
