<script lang="ts">
	import { ArrowDownTray, ClipboardDocument, EllipsisHorizontal, Icon, Play } from 'svelte-hero-icons';
	import { resolve } from '$app/paths';
	import { convertTimeSpanStringToSeconds, getTimeString, heroArtist, sourceOf } from '$lib';
	import type { SearchResult } from '$states/search.svelte';
	import queue from '$states/queue.svelte';
	import session from '$states/session.svelte';
	import ArtistLink from '$components/ArtistLink.svelte';


	const { result }: { result: SearchResult } = $props();
	let duration = $derived(getTimeString(convertTimeSpanStringToSeconds(result.duration)));
	let isLong = $derived(convertTimeSpanStringToSeconds(result.duration) > 15 * 60);
	let source = $derived(sourceOf(result.id));
	// The menu has room for one artist, so it takes the first of a joined credit — the rest are
	// each their own link on the row itself.
	let artistUrl = $derived(
		`${resolve('/artist')}?term=${encodeURIComponent(heroArtist(result.artist))}`
	);

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

	// An open menu over an action that happened somewhere else reads as nothing
	// happening — on a phone the menu is most of the row.
	function closeMenu(event: Event) {
		(event.currentTarget as HTMLElement).closest('details')?.removeAttribute('open');
	}

	function playNext(event: Event) {
		event.stopPropagation();
		queue.playNext(result);
		closeMenu(event);
	}

	let copied = $state(false);
	let settle: ReturnType<typeof setTimeout>;

	async function copyId(event: Event) {
		event.stopPropagation();
		await navigator.clipboard.writeText(result.id);
		// nothing about a copy is visible anywhere else, so the menu stays open
		// long enough to say so and then takes itself away
		copied = true;
		const menu = event.currentTarget as HTMLElement;
		clearTimeout(settle);
		settle = setTimeout(() => {
			copied = false;
			menu.closest('details')?.removeAttribute('open');
		}, 900);
	}

	function handleKeydown(event: KeyboardEvent) {
		if (event.target !== event.currentTarget || (event.key !== 'Enter' && event.key !== ' ')) return;
		event.preventDefault();
		playNow();
	}
</script>

<div
	class="group grid cursor-pointer grid-cols-[2.75rem_minmax(0,1fr)_auto] items-center gap-3 rounded-row px-2 py-2 transition-colors hover:bg-surface-100 active:bg-surface-200 focus-visible:bg-surface-100 focus-visible:outline-none sm:grid-cols-[2.75rem_minmax(0,1fr)_auto_auto] sm:gap-3.5 sm:px-2.5"
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
			<ArtistLink artist={result.artist} />{#if result.album} · {result.album}{/if}<!--
			--><span class="font-mono sm:hidden"> · {source.name} · {duration}{isLong ? ' · long' : ''}</span>
		</p>
	</div>

	<!-- its own column is a luxury a 320px row cannot afford; the artist line
	     carries the same two facts instead -->
	<div class="hidden items-center gap-2.5 sm:flex">
		<!-- Solid, unlike the outlined "long" beside it: this one is the row's identity rather than a
		     remark about it, and it is the same fill the home page's cards wear. -->
		<span
			class="rounded-full px-1.5 py-px font-mono text-[0.6rem] tracking-[0.09em] {source.badge}"
			>{source.name}</span
		>
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
		{#if !session.inRoom}
			<button
				type="button"
				class="hidden items-center gap-1 rounded-[5px] bg-primary-600 px-2.5 py-1 text-xs font-semibold text-white focus-visible:outline-2 focus-visible:outline-primary-200 sm:inline-flex"
				onclick={handlePlayNow}
			>
				<Icon src={Play} mini size="14" /> <span>Play</span>
			</button>
		{/if}
		<button
			type="button"
			aria-label="Add {result.name} to queue"
			class="hidden rounded-[5px] px-2.5 py-1 text-xs font-semibold focus-visible:outline-2 focus-visible:outline-primary-200 sm:block {session.inRoom
				? 'bg-primary-600 text-white'
				: 'border border-haze text-chalk hover:bg-surface-200'}"
			onclick={addToQueue}
		>
			Queue
		</button>
		<details class="relative">
			<summary
				aria-label="More actions for {result.name}"
				class="flex size-11 list-none items-center justify-center rounded-[5px] border border-haze text-fog hover:bg-surface-200 hover:text-chalk focus-visible:outline-2 focus-visible:outline-primary-200 sm:size-7 [&::-webkit-details-marker]:hidden"
				onclick={stopPropagation}
				onkeydown={stopPropagation}
			>
				<Icon src={EllipsisHorizontal} mini size="16" />
			</summary>
			<div
				class="absolute right-0 z-20 mt-1 grid w-44 gap-0.5 rounded-panel border border-haze bg-surface-100 p-1 text-left"
			>
				<button
					type="button"
					class="flex min-h-10 items-center rounded-art px-2 text-left text-xs hover:bg-surface-200"
					onclick={playNext}
				>
					Play next
				</button>
				<!-- room queue items carry no contentUrl; hide the action rather than
				     linking nowhere -->
				{#if result.contentUrl}
					<a
						href={result.contentUrl}
						download
						class="flex min-h-10 items-center gap-2 rounded-art px-2 text-xs hover:bg-surface-200"
					>
						<Icon src={ArrowDownTray} mini size="14" /> Download raw
					</a>
				{/if}
				<button
					type="button"
					class="flex min-h-10 items-center gap-2 rounded-art px-2 text-left text-xs hover:bg-surface-200"
					onclick={copyId}
				>
					<Icon src={ClipboardDocument} mini size="14" />
					{copied ? 'Copied' : 'Copy id'}
				</button>
				<a
					href={artistUrl}
					class="flex min-h-10 items-center rounded-art px-2 text-xs hover:bg-surface-200"
				>
					Go to artist
				</a>
			</div>
		</details>
	</div>
</div>
