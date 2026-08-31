<script lang="ts">
	import type { PageData } from './$types';
	import SearchRow from '$components/search/SearchRow.svelte';

	const { data }: { data: PageData } = $props();
	let activeTab = $state<'library' | 'youtube'>('library');
	let activeResults = $derived(activeTab === 'library' ? data.localResults : data.youtubeResults);
</script>

<div class="page mx-auto max-w-5xl gap-6 p-4 pb-36 sm:pb-28 sm:p-6">
	<div>
		<p class="eyebrow text-gold">Artist</p>
		<h1 class="mt-2 font-display text-2xl font-light leading-tight tracking-tight text-chalk sm:text-3xl">{data.term || 'Choose an artist'}</h1>
	</div>

	{#if data.term}
		<div class="flex w-fit rounded-panel border border-haze bg-surface-0 p-1" role="tablist" aria-label="Artist source">
			<button type="button" role="tab" aria-selected={activeTab === 'library'} class={`rounded-row px-3 py-1.5 text-sm font-semibold text-fog hover:text-chalk ${activeTab === 'library' ? 'bg-primary-600 text-white' : ''}`} onclick={() => (activeTab = 'library')}>In the library · {data.localResults.length}</button>
			<button type="button" role="tab" aria-selected={activeTab === 'youtube'} class={`rounded-row px-3 py-1.5 text-sm font-semibold text-fog hover:text-chalk ${activeTab === 'youtube' ? 'bg-primary-600 text-white' : ''}`} onclick={() => (activeTab = 'youtube')}>From YouTube · {data.youtubeResults.length}</button>
		</div>

		{#if activeResults.length > 0}
			<div class="flex flex-col">
				{#each activeResults as result (result.id)}
					<SearchRow {result} />
				{/each}
			</div>
		{:else}
			<p class="text-fog">No {activeTab === 'library' ? 'library' : 'YouTube'} tracks matched this artist.</p>
		{/if}
	{:else}
		<p class="max-w-lg text-fog">Search for an artist, or choose one from the library on the home page.</p>
	{/if}
</div>
