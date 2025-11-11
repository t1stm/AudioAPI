<script lang="ts">
	import type { PageData } from './$types';
	import type { SearchResult } from '$states/search.svelte';
	import Song from '$components/home/song/Song.svelte';
	import { getRandomSongs } from '$requests/songs';
	const { data }: { data: PageData } = $props();

	let songs = $state(data.songs as SearchResult[]);
	const updateRandomSongs = () => {
		getRandomSongs(fetch).then((result) => (songs = result as SearchResult[]));
	};
</script>

<div class="flex flex-col gap-4 p-4 pb-16 rounded-lg w-full h-full">
	<div class="flex gap-8 h-10 items-center">
		<span class="font-bold text-xl text-white">(Curated) Picks</span>
		<button
			class="ml-auto text-white text-xs font-bold bg-primary-0 box-border p-2 rounded-lg cursor-pointer"
			onclick={updateRandomSongs}>Get More</button
		>
	</div>
	<div
		class="grid grid-flow-col-dense grid-rows-2 gap-8 w-full h-auto box-border overflow-x-auto p-2 rounded-lg"
	>
		{#each songs as song (song.id)}
			<Song {song} />
		{/each}
	</div>
</div>
