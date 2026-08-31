<script lang="ts">
	import { ArrowDownTray, ClipboardDocument, EllipsisHorizontal, Icon, Play } from 'svelte-hero-icons';
	import { resolve } from '$app/paths';
	import { convertTimeSpanStringToSeconds, getTimeString } from '$lib';
	import type { SearchResult } from '$states/search.svelte';
	import queue from '$states/queue.svelte';
	import ArtistLink from '$components/ArtistLink.svelte';


	const { result }: { result: SearchResult } = $props();
	let duration = $derived(getTimeString(convertTimeSpanStringToSeconds(result.duration)));
	let isLong = $derived(convertTimeSpanStringToSeconds(result.duration) > 15 * 60);
	let artistUrl = $derived(`${resolve('/artist')}?term=${encodeURIComponent(result.artist)}`);

	function stopPropagation(event: Event) {
		event.stopPropagation();
	}

	function playNow() {
		queue.playNow(result);
	}

	// Anything inside the row that is a link (the artist name, the download) owns
	// its own click. Stopping propagation there instead would hide the click from
	// SvelteKit's router, which listens on document.documentElement — the link
	// would fall back to a full page load and wipe the queue.
	function playUnlessLink(event: MouseEvent) {
		if ((event.target as HTMLElement).closest('a')) return;
		playNow();
	}

	function handlePlayNow(event: Event) {
		event.stopPropagation();
		playNow();
	}

	function addToQueue(event: Event) {
		event.stopPropagation();
		queue.add(result);
	}

	function playNext(event: Event) {
		event.stopPropagation();
		queue.playNext(result);
	}

	async function copyId(event: Event) {
		event.stopPropagation();
		await navigator.clipboard.writeText(result.id);
	}

	function handleKeydown(event: KeyboardEvent) {
		if (event.target !== event.currentTarget || (event.key !== 'Enter' && event.key !== ' ')) return;
		event.preventDefault();
		playNow();
	}
</script>

<div
	class="group grid cursor-pointer grid-cols-[2.75rem_minmax(0,1fr)_auto_auto] items-center gap-3.5 rounded-row px-2.5 py-2 transition-colors hover:bg-surface-100 focus-visible:bg-surface-100 focus-visible:outline-none"
	role="button"
	tabindex="0"
	onclick={playUnlessLink}
	onkeydown={handleKeydown}
>
	<img
		src={result.thumbnailUrl ?? '/empty.png'}
		alt=""
		class="size-11 rounded-art object-cover"
		onerror={(event: Event) => {
			const image = event.currentTarget as HTMLImageElement;
			if (!image.src.endsWith('/empty.png')) image.src = '/empty.png';
		}}
	/>
	<div class="min-w-0">
		<p class="line-clamp-2 text-sm font-medium leading-snug text-chalk">{result.name}</p>
		<p class="truncate text-[0.79rem] text-fog">
			<ArtistLink artist={result.artist} />{#if result.album} · {result.album}{/if}
		</p>
	</div>

	<div class="flex items-center gap-2.5">
		{#if isLong}
			<span
				class="rounded-full border border-gold/45 px-1.5 py-px font-mono text-[0.6rem] uppercase tracking-[0.09em] text-gold"
				>long</span
			>
		{/if}
		<span class="w-14 text-right font-mono text-[0.79rem] text-fog">{duration}</span>
	</div>

	<div
		class="flex items-center justify-end gap-1.5 sm:opacity-0 sm:transition-opacity sm:group-hover:opacity-100 sm:group-focus-within:opacity-100"
	>
		<button
			type="button"
			class="hidden items-center gap-1 rounded-[5px] bg-primary-600 px-2.5 py-1 text-xs font-semibold text-white focus-visible:outline-2 focus-visible:outline-primary-200 sm:inline-flex"
			onclick={handlePlayNow}
		>
			<Icon src={Play} mini size="14" /> <span>Play</span>
		</button>
		<button
			type="button"
			aria-label="Add {result.name} to queue"
			class="hidden rounded-[5px] border border-haze px-2.5 py-1 text-xs font-semibold text-chalk hover:bg-surface-200 focus-visible:outline-2 focus-visible:outline-primary-200 sm:block"
			onclick={addToQueue}
		>
			Queue
		</button>
		<details class="relative">
			<summary
				aria-label="More actions for {result.name}"
				class="list-none rounded-[5px] border border-haze p-1 text-fog hover:bg-surface-200 hover:text-chalk focus-visible:outline-2 focus-visible:outline-primary-200 [&::-webkit-details-marker]:hidden"
				onclick={stopPropagation}
				onkeydown={stopPropagation}
			>
				<Icon src={EllipsisHorizontal} mini size="16" />
			</summary>
			<div
				class="absolute right-0 z-20 mt-1 grid w-40 gap-0.5 rounded-panel border border-haze bg-surface-100 p-1 text-left"
			>
				<button
					type="button"
					class="rounded-art px-2 py-1.5 text-left text-xs hover:bg-surface-200"
					onclick={playNext}
				>
					Play next
				</button>
				<a
					href={result.contentUrl}
					download
					class="flex items-center gap-2 rounded-art px-2 py-1.5 text-xs hover:bg-surface-200"
				>
					<Icon src={ArrowDownTray} mini size="14" /> Download raw
				</a>
				<button
					type="button"
					class="flex items-center gap-2 rounded-art px-2 py-1.5 text-left text-xs hover:bg-surface-200"
					onclick={copyId}
				>
					<Icon src={ClipboardDocument} mini size="14" /> Copy id
				</button>
				<a
					href={artistUrl}
					class="rounded-art px-2 py-1.5 text-xs hover:bg-surface-200"
				>
					Go to artist
				</a>
			</div>
		</details>
	</div>
</div>
