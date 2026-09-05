<script lang="ts">
	import type { PageData } from './$types';
	import type { SearchResult } from '$states/search.svelte';
	import SearchRow from '$components/search/SearchRow.svelte';
	import RowSkeleton from '$components/RowSkeleton.svelte';

	const { data }: { data: PageData } = $props();

	let localResults = $state<SearchResult[]>([]);
	let youtubeResults = $state<SearchResult[]>([]);
	let localLoading = $state(Boolean(data.term));
	let youtubeLoading = $state(Boolean(data.term));

	// The library is the tab this page opens on, but an artist the library has never
	// heard of should not open on an empty one. Until the library side has answered
	// there is nothing to move away from, and a tab pressed by hand is never moved.
	let activeTab = $state<'library' | 'youtube'>('library');
	let chosen = false;
	let activeResults = $derived(activeTab === 'library' ? localResults : youtubeResults);
	let activeLoading = $derived(activeTab === 'library' ? localLoading : youtubeLoading);
	let waiting = $derived(activeLoading ? Math.max(0, 8 - activeResults.length) : 0);

	function choose(tab: 'library' | 'youtube') {
		chosen = true;
		activeTab = tab;
	}

	/** A side's count while it is still arriving is not a count yet. */
	function count(results: SearchResult[], loading: boolean) {
		return loading ? '…' : String(results.length);
	}

	// Takes a push rather than the list itself, so the effect below never reads the state it just
	// reset — a synchronous read of that would make the effect its own dependency.
	async function fill(
		stream: AsyncIterable<SearchResult>,
		alive: () => boolean,
		push: (result: SearchResult) => void
	) {
		for await (const result of stream) {
			if (!alive()) return;
			push(result);
		}
	}

	// An effect rather than onMount: opening another artist is a navigation to this same route, so
	// this component is reused and only `data` changes. onMount would fire once and leave the
	// previous artist's rows on screen under the new artist's name.
	$effect(() => {
		const local = data.localResults;
		const youtube = data.youtubeResults;

		// Back to what a fresh load of this page would show — including the tab, since the artist
		// whose empty library tab was worth stepping around is no longer the one on screen.
		localResults = [];
		youtubeResults = [];
		activeTab = 'library';
		chosen = false;
		localLoading = Boolean(local);
		youtubeLoading = Boolean(youtube);
		if (!local || !youtube) return;

		// An artist abandoned mid-stream keeps arriving; `live` is what stops those results being
		// pushed into the lists the next artist is filling.
		let live = true;
		const alive = () => live;

		fill(local, alive, (result) => localResults.push(result))
			.catch(() => {})
			.finally(() => {
				if (!live) return;
				localLoading = false;
				if (!chosen && localResults.length === 0) activeTab = 'youtube';
			});
		fill(youtube, alive, (result) => youtubeResults.push(result))
			.catch(() => {})
			.finally(() => {
				if (live) youtubeLoading = false;
			});

		return () => (live = false);
	});
</script>

<svelte:head><title>{data.term ? `${data.term} · musicrain` : 'Artist · musicrain'}</title></svelte:head>

<div class="page mx-auto w-full max-w-5xl gap-5 p-4 sm:gap-6 sm:p-6 sm:pb-28">
	<div>
		<p class="eyebrow text-gold">Artist</p>
		<h1 class="mt-2 font-display text-xl font-light leading-tight tracking-tight text-chalk sm:text-3xl">{data.term || 'Choose an artist'}</h1>
	</div>

	{#if data.term}
		<div class="flex w-full rounded-panel border border-haze bg-surface-0 p-1 sm:w-fit" role="tablist" aria-label="Artist source">
			<button type="button" role="tab" aria-selected={activeTab === 'library'} class={`min-h-10 flex-1 rounded-row px-3 py-1.5 text-sm font-semibold text-fog hover:text-chalk sm:flex-none ${activeTab === 'library' ? 'bg-primary-600 text-white' : ''}`} onclick={() => choose('library')}><span class="hidden sm:inline">In the library</span><span class="sm:hidden">Library</span> · {count(localResults, localLoading)}</button>
			<button type="button" role="tab" aria-selected={activeTab === 'youtube'} class={`min-h-10 flex-1 rounded-row px-3 py-1.5 text-sm font-semibold text-fog hover:text-chalk sm:flex-none ${activeTab === 'youtube' ? 'bg-primary-600 text-white' : ''}`} onclick={() => choose('youtube')}><span class="hidden sm:inline">From YouTube</span><span class="sm:hidden">YouTube</span> · {count(youtubeResults, youtubeLoading)}</button>
		</div>

		{#if activeResults.length > 0 || waiting > 0}
			<div class="flex flex-col" aria-busy={activeLoading}>
				{#each activeResults as result (result.id)}
					<SearchRow {result} />
				{/each}
				{#each [...Array(waiting).keys()] as row (row)}
					<RowSkeleton />
				{/each}
			</div>
			{#if activeLoading}<p class="sr-only" aria-live="polite">Loading this artist's tracks.</p>{/if}
		{:else}
			<p class="text-fog">No {activeTab === 'library' ? 'library' : 'YouTube'} tracks matched this artist.</p>
		{/if}
	{:else}
		<p class="max-w-lg text-fog">Search for an artist, or choose one from the library on the home page.</p>
	{/if}
</div>
