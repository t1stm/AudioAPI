<script lang="ts">
  import { type PageData } from './$types';
	import SearchRow from '$components/search/SearchRow.svelte';

	const { data }: { data: PageData } = $props();
	let results = $derived(data.results);
	let libraryResults = $derived(results.filter((result) => result.id.startsWith('audio://')));
	let youtubeResults = $derived(results.filter((result) => !result.id.startsWith('audio://')));
</script>

<div class="page mx-auto w-full max-w-5xl gap-6 p-4 sm:gap-9 sm:p-6 sm:pb-28">
	<h1 class="font-display text-lg font-light leading-tight tracking-tight text-chalk sm:text-2xl">
		{results.length} results for “{data.term}”
	</h1>

	{#if results.length === 0}
		<p class="max-w-lg text-fog">
			Nothing matched <strong class="font-semibold text-chalk">{data.term}</strong>. Try an artist
			name, or paste a YouTube link.
		</p>
	{:else}
		{#if libraryResults.length > 0}
			<section class="flex flex-col gap-2">
				<h2 class="eyebrow flex items-center gap-3 text-gold">
					In the library · {libraryResults.length}
					<span class="h-px flex-1 bg-gold/35"></span>
				</h2>
				<div class="flex flex-col">
					{#each libraryResults as result (result.id)}
						<SearchRow {result} />
					{/each}
				</div>
			</section>
		{/if}

		{#if youtubeResults.length > 0}
			<section class="flex flex-col gap-2">
				<h2 class="eyebrow flex items-center gap-3">
					From YouTube · {youtubeResults.length}
					<span class="h-px flex-1 bg-haze"></span>
				</h2>
				<div class="flex flex-col">
					{#each youtubeResults as result (result.id)}
						<SearchRow {result} />
					{/each}
				</div>
			</section>
		{/if}
	{/if}
</div>
