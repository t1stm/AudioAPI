<script lang="ts">
	import '../../app.css';
	import Header from '$components/header/Header.svelte';
	import Player from '$components/player/Player.svelte';
	let { children } = $props();
</script>

<div class="flex flex-col w-full h-svh overflow-hidden">
	<Header />
	<main class="flex flex-col relative h-full bg-dark-0 m-2 mt-0 rounded-lg">
		{@render children()}
	</main>
	<Player />
</div>
