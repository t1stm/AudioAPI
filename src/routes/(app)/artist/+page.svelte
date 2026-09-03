<script lang="ts">
	import type { PageData } from './$types';
	import SearchRow from '$components/search/SearchRow.svelte';

	const { data }: { data: PageData } = $props();
	let activeTab = $state<'library' | 'youtube'>(data.localResults.length === 0 ? 'youtube' : 'library');
	let activeResults = $derived(activeTab === 'library' ? data.localResults : data.youtubeResults);
</script>

<div class="page mx-auto w-full max-w-5xl gap-5 p-4 sm:gap-6 sm:p-6 sm:pb-28">
	<div>
		<p class="eyebrow text-gold">Artist</p>
		<h1 class="mt-2 font-display text-xl font-light leading-tight tracking-tight text-chalk sm:text-3xl">{data.term || 'Choose an artist'}</h1>
	</div>

	{#if data.term}
		<div class="flex w-full rounded-panel border border-haze bg-surface-0 p-1 sm:w-fit" role="tablist" aria-label="Artist source">
			<button type="button" role="tab" aria-selected={activeTab === 'library'} class={`min-h-10 flex-1 rounded-row px-3 py-1.5 text-sm font-semibold text-fog hover:text-chalk sm:flex-none ${activeTab === 'library' ? 'bg-primary-600 text-white' : ''}`} onclick={() => (activeTab = 'library')}><span class="hidden sm:inline">In the library</span><span class="sm:hidden">Library</span> · {data.localResults.length}</button>
			<button type="button" role="tab" aria-selected={activeTab === 'youtube'} class={`min-h-10 flex-1 rounded-row px-3 py-1.5 text-sm font-semibold text-fog hover:text-chalk sm:flex-none ${activeTab === 'youtube' ? 'bg-primary-600 text-white' : ''}`} onclick={() => (activeTab = 'youtube')}><span class="hidden sm:inline">From YouTube</span><span class="sm:hidden">YouTube</span> · {data.youtubeResults.length}</button>
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
