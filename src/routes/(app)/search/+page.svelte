<script lang="ts">
	import { type PageData } from './$types';
	import Song from '$components/home/song/Song.svelte';

	const { data }: { data: PageData } = $props();
	let results = $derived(data.results);
</script>

<div class="flex flex-col gap-4 p-6 pb-12 w-full">
	<span class="text-2xl text-white font-bold">Results</span>
	<div class="flex gap-2 w-full overflow-auto pb-4">
		{#each results as song (song.id)}
			<Song {song} />
		{/each}
	</div>
</div>
