<script lang="ts">
	import { resolve } from '$app/paths';
	import { convertTimeSpanStringToSeconds, getTimeString } from '$lib';
	import audio from '$states/audio.svelte';
	import current from '$states/current.svelte';
	import queue from '$states/queue.svelte';
	import type { SearchResult } from '$states/search.svelte';
	import ArtistLink from '$components/ArtistLink.svelte';


	let items = $derived(queue.items);
	let currentIndex = $derived(queue.currentIndex);
	let currentItem = $derived(items[currentIndex]);
	let nextItems = $derived(items.slice(currentIndex + 1));
	let playedItems = $derived(items.slice(0, currentIndex));
	let showPlayed = $state(false);
	let dragIndex = $state<number | null>(null);
	let progress = $derived(
		current.lengthSeconds > 0 ? Math.min((audio.currentSeconds / current.lengthSeconds) * 100, 100) : 0
	);
	let remainingSeconds = $derived(
		Math.max(current.lengthSeconds - audio.currentSeconds, 0) +
			nextItems.reduce((total, item) => total + convertTimeSpanStringToSeconds(item.duration), 0)
	);
	let remainingTime = $derived(getTimeString(remainingSeconds));

	function remove(index: number, event: MouseEvent) {
		event.stopPropagation();
		queue.removeIndex(index);
	}

	function play(index: number) {
		queue.playIndex(index);
	}

	// see ArtistLink: the artist name is a real link, so the row must not act on it
	function playUnlessLink(index: number, event: MouseEvent) {
		if ((event.target as HTMLElement).closest('a')) return;
		play(index);
	}

	function dragStart(index: number, event: DragEvent) {
		dragIndex = index;
		if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
	}

	function drop(event: DragEvent) {
		event.preventDefault();
		if (dragIndex !== null) queue.setNext(dragIndex);
		dragIndex = null;
	}

	function imageFallback(event: Event) {
		const image = event.currentTarget as HTMLImageElement;
		if (!image.src.endsWith('/empty.png')) image.src = '/empty.png';
	}

	function itemLabel(item: SearchResult) {
		return `${item.name} by ${item.artist}`;
	}
</script>

<section class="flex h-full flex-col overflow-hidden rounded-panel border border-haze bg-surface-100/95 p-3 text-chalk backdrop-blur-xl">
	<div class="flex items-center justify-between border-b border-haze pb-2">
		<h2 class="eyebrow text-primary-500">Queue</h2>
		<span class="font-mono text-xs text-fog">{items.length} tracks</span>
	</div>

{#if items.length === 0}
		<p class="mt-4 max-w-64 text-sm text-fog">
			Queue’s empty. Add something from <a class="text-primary-500 underline-offset-4 hover:underline" href={resolve('/search')}>search</a>, or
			<a class="text-primary-500 underline-offset-4 hover:underline" href={resolve('/')}>roll a track</a> on the home page.
		</p>
	{:else}
	<section class="py-3">
		<h3 class="eyebrow mb-2">Now playing</h3>
		{#if currentItem}
			<div class="flex items-center gap-3">
				<img src={currentItem.thumbnailUrl ?? '/empty.png'} alt="" class="size-14 rounded-art object-cover" onerror={imageFallback} />
				<div class="min-w-0 flex-1">
					<p class="truncate text-sm font-semibold">{currentItem.name}</p>
					<p class="truncate text-xs text-fog"><ArtistLink artist={currentItem.artist} /></p>
					<div class="mt-2 h-px overflow-hidden bg-haze">
						<div class="h-full bg-primary-500" style:width={progress + '%'}></div>
					</div>
				</div>
			</div>
		{:else}
			<p class="text-sm text-fog">Pick a track to start listening.</p>
		{/if}
	</section>

	<section class="flex min-h-0 flex-1 flex-col border-t border-haze py-3">
		<h3 class="eyebrow mb-2">Next up · {nextItems.length}</h3>
		{#if nextItems.length > 0}
			<ul class="min-h-0 flex-1 space-y-1 overflow-y-auto pr-1" ondragover={(event) => event.preventDefault()} ondrop={drop}>
				{#each nextItems as item, offset (item.id)}
					{@const index = currentIndex + offset + 1}
					<li
						draggable="true"
						class="group flex cursor-grab items-center gap-2 rounded-[5px] px-1 py-1.5 hover:bg-surface-0 active:cursor-grabbing"
						ondragstart={(event) => dragStart(index, event)}
						ondblclick={(event) => playUnlessLink(index, event)}
						title={`Double-click to play ${itemLabel(item)}`}
					>
						<span class="w-4 text-right font-mono text-[0.68rem] text-fog">{offset + 1}</span>
						<img src={item.thumbnailUrl ?? '/empty.png'} alt="" class="size-9 rounded-art object-cover" onerror={imageFallback} />
						<div class="min-w-0 flex-1">
							<p class="truncate text-sm">{item.name}</p>
							<p class="truncate text-xs text-fog"><ArtistLink artist={item.artist} /></p>
						</div>
						<button
							type="button"
							aria-label={`Remove ${itemLabel(item)} from queue`}
							class="rounded-art p-1 text-fog opacity-0 hover:bg-surface-200 hover:text-chalk focus-visible:opacity-100 group-hover:opacity-100"
							onclick={(event) => remove(index, event)}
						>
							×
						</button>
					</li>
				{/each}
			</ul>
		{:else}
			<p class="text-sm text-fog">Nothing queued after this track.</p>
		{/if}
	</section>

	<section class="border-t border-haze py-3">
		<button type="button" class="eyebrow flex w-full items-center justify-between text-left hover:text-chalk" onclick={() => (showPlayed = !showPlayed)}>
			<span>Played · {playedItems.length}</span>
			<span aria-hidden="true">{showPlayed ? '−' : '+'}</span>
		</button>
		{#if showPlayed && playedItems.length > 0}
			<ul class="mt-2 max-h-28 space-y-1 overflow-y-auto">
				{#each playedItems as item, index (item.id)}
					<li class="flex items-center gap-2 px-1 py-1">
						<span class="w-4 text-right font-mono text-[0.68rem] text-fog">{index + 1}</span>
						<span class="truncate text-xs text-fog">{item.name}</span>
					</li>
				{/each}
			</ul>
		{/if}
	</section>

	<footer class="flex items-center gap-2 border-t border-haze pt-3">
		<span class="mr-auto font-mono text-[0.68rem] uppercase tracking-[0.13em] text-fog">{items.length} tracks · {remainingTime} left</span>
		<button type="button" class="rounded-[5px] border border-haze px-2 py-1 text-xs font-semibold hover:bg-surface-200" onclick={() => queue.shuffle()}>
			Shuffle
		</button>
		<button type="button" class="rounded-[5px] border border-haze px-2 py-1 text-xs font-semibold text-fog hover:bg-surface-200 hover:text-chalk" onclick={() => queue.clear()}>
			Clear
		</button>
	</footer>
	{/if}
</section>
