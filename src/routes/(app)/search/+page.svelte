<script lang="ts">
	import { onMount } from 'svelte';
	import { type PageData } from './$types';
	import type { SearchResult } from '$states/search.svelte';
	import SearchRow from '$components/search/SearchRow.svelte';
	import RowSkeleton from '$components/RowSkeleton.svelte';

	const { data }: { data: PageData } = $props();

	let results = $state<SearchResult[]>([]);
	let searching = $state(true);
	// A result's section is only known once it is here, so the ones still coming wait
	// in a block of their own below rather than in a section that may not be theirs.
	let waiting = $derived(searching ? Math.max(0, 8 - results.length) : 0);
	let libraryResults = $derived(results.filter((result) => result.id.startsWith('audio://')));
	let youtubeResults = $derived(results.filter((result) => !result.id.startsWith('audio://')));

	onMount(async () => {
		if (!data.results) return (searching = false);
		try {
			for await (const result of data.results) results.push(result);
		} finally {
			searching = false;
		}
	});
</script>

<svelte:head><title>{data.term ? `${data.term} · musicrain` : 'Search · musicrain'}</title></svelte:head>

<div class="page mx-auto w-full max-w-5xl gap-6 p-4 sm:gap-9 sm:p-6 sm:pb-28">
	<h1 class="font-display text-lg font-light leading-tight tracking-tight text-chalk sm:text-2xl">
		{#if searching}
			Searching for “{data.term}”…
		{:else}
			{results.length} results for “{data.term}”
		{/if}
	</h1>

	{#if !searching && results.length === 0}
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

		{#if waiting > 0}
			<div class="flex flex-col" aria-busy="true">
				{#each [...Array(waiting).keys()] as row (row)}
					<RowSkeleton />
				{/each}
			</div>
			<p class="sr-only" aria-live="polite">Searching.</p>
		{/if}
	{/if}
</div>
