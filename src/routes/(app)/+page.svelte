<script lang="ts">
	import { goto } from '$app/navigation';
	import { resolve } from '$app/paths';
	import { onMount } from 'svelte';
	import type { PageData } from './$types';
	import type { SearchResult } from '$states/search.svelte';
	import queue from '$states/queue.svelte';
	import session from '$states/session.svelte';
	import { getRecentlyPlayed } from '$lib/recentlyPlayed';
	import { AudioApiError, findQueryType, getArtistLocal, getLocalVariant, getRandomSongs } from '$requests/songs';
	import type { LocalVariant } from '$requests/songs';
	import { convertTimeSpanStringToSeconds, getTimeString } from '$lib';
	import { SliderInteractions } from '$lib/sliderInteractions.svelte.js';
	import Song from '$components/home/song/Song.svelte';
	import ArtistLink from '$components/ArtistLink.svelte';

	import { Icon, Link, Play, ArrowPath } from 'svelte-hero-icons';

	const { data }: { data: PageData } = $props();

	let hero = $state<SearchResult | null>(data.hero);
	let curated = $state<SearchResult[]>(data.songs);
	let artistSongs = $state<SearchResult[]>(data.artistSongs);
	let recentlyPlayed = $state<SearchResult[]>([]);
	let rolling = $state(false);
	let rollingPicks = $state(false);
	// The roll's odds, not a level: both sources are always drawn from, so the bar
	// is always full and the handle is the seam between the two. The API defaults
	// to 40 when the param is absent. 5% detents keep the readout tidy.
	// The handle's position is the library share — it is the left territory, so
	// the pointer and the seam move together and ArrowRight means "more library".
	const odds = new SliderInteractions(5, 60);
	let libraryPercent = $derived(Math.round(odds.percentage / 5) * 5);
	let youTubePercent = $derived(100 - libraryPercent);
	let pastedQuery = $state('');
	let resolving = $state(false);
	let pasteMessage = $state('');
	let pasteError = $state('');
	// Counts come from the one 200-track sample the page already loads, so the
	// heaviest names in the library surface first without a second request.
	let artists = $derived(
		Object.entries(
			data.librarySongs.reduce<Record<string, number>>((tally, song) => {
				if (song.artist) tally[song.artist] = (tally[song.artist] ?? 0) + 1;
				return tally;
			}, {})
		).sort(([nameA, countA], [nameB, countB]) => countB - countA || nameA.localeCompare(nameB))
	);
	const artistUrl = (name: string) => `${resolve('/artist')}?term=${encodeURIComponent(name)}`;
	let heroDuration = $derived(hero ? getTimeString(convertTimeSpanStringToSeconds(hero.duration)) : '');
	let heroInLibrary = $derived(hero?.id.startsWith('audio://') ?? false);
	// ponytail: the result contract carries no format field. For library tracks the
	// raw file's extension is the only signal there is; YouTube results have none
	// to give, so they get no tag. Drop this once the API returns a real format.
	const formatOf = (song: SearchResult | null) =>
		(song?.id.startsWith('audio://') && song.contentUrl?.split('?')[0].match(/\.(\w{2,4})$/)?.[1].toUpperCase()) ||
		'';
	let heroFormat = $derived(formatOf(hero));

	// What the library says about this roll, and which button is waiting on an answer.
	// Nothing is shown until a press: the hero looks exactly as it did.
	let variant = $state<LocalVariant | null>(null);
	let pending = $state<'play' | 'queue' | null>(null);
	// A fast second roll must not have the first roll's suggestion land on it. The
	// lookup is not awaited by rollAgain, so the roll stays as quick as it was.
	let rollToken = 0;
	let promptFirst = $state<HTMLButtonElement | null>(null);
	let pressed: HTMLElement | null = null;

	let verb = $derived(pending === 'play' ? 'Play' : 'Add');
	// One word for the library side rather than a synonym table: "the original" reads
	// right against every rendition tag, and the tag itself names the other button.
	let rendition = $derived(variant?.youTubeTags.join(' ') ?? '');
	let delta = $derived(variant?.durationDeltaSeconds ?? 0);
	let deltaText = $derived(delta === 0 ? '' : `${Math.abs(delta)}s ${delta < 0 ? 'shorter' : 'longer'}`);
	let eyebrow = $derived(
		variant?.match === 'variant'
			? 'The original is in your library'
			: variant?.match === 'weak'
				? 'Possibly the same track'
				: 'In your library'
	);
	let variantLine = $derived(
		variant
			? [
					`${variant.result.name} — ${variant.result.artist}`,
					formatOf(variant.result),
					getTimeString(convertTimeSpanStringToSeconds(variant.result.duration)),
					variant.match === 'weak' ? deltaText : ''
				]
					.filter(Boolean)
					.join(' · ')
			: ''
	);

	$effect(() => {
		if (pending && promptFirst) promptFirst.focus();
	});

	onMount(() => {
		recentlyPlayed = getRecentlyPlayed();
		if (hero) lookUpVariant(hero, rollToken);
	});

	async function lookUpVariant(song: SearchResult, token: number) {
		// A suggestion that fails to load is a hero with no prompt, never an error.
		const found = await getLocalVariant(song, fetch).catch(() => null);
		if (token === rollToken) variant = found;
	}

	function imageFallback(event: Event) {
		const image = event.currentTarget as HTMLImageElement;
		if (!image.src.endsWith('/empty.png')) image.src = '/empty.png';
	}

	async function rollAgain() {
		if (rolling) return;
		rolling = true;
		variant = null;
		pending = null;
		const token = ++rollToken;
		try {
			const nextHero = (await getRandomSongs(fetch, 1, youTubePercent / 100))[0] ?? null;
			hero = nextHero;
			artistSongs = nextHero ? await getArtistLocal(nextHero.artist, fetch) : [];
			if (nextHero) lookUpVariant(nextHero, token);
		} finally {
			rolling = false;
		}
	}

	async function rollPicks() {
		if (rollingPicks) return;
		rollingPicks = true;
		try {
			curated = await getRandomSongs(fetch, 30, youTubePercent / 100);
		} finally {
			rollingPicks = false;
		}
	}

	function press(which: 'play' | 'queue', event: MouseEvent) {
		if (!hero) return;
		if (variant) {
			pressed = event.currentTarget as HTMLElement;
			pending = which;
			return;
		}
		act(which, hero);
	}

	function choose(useVariant: boolean) {
		const song = useVariant ? variant?.result : hero;
		const which = pending;
		close();
		if (song && which) act(which, song);
	}

	function act(which: 'play' | 'queue', song: SearchResult) {
		// In a room queue.add sends `add <id>`, so swapping the id means the room
		// streams a local file instead of every listener going out to YouTube.
		if (which === 'play') queue.playNow(song);
		else queue.add(song);
	}

	/** Escape restores the original row and the focus to the button that was pressed. */
	function escapeCloses(event: KeyboardEvent) {
		if (event.key === 'Escape') close();
	}

	function close() {
		pending = null;
		pressed?.focus();
		pressed = null;
	}

	async function resolvePaste() {
		const value = pastedQuery.trim();
		if (!value || resolving) return;

		resolving = true;
		pasteError = '';
		pasteMessage = '';
		try {
			const resolved = await findQueryType(value);
			if (resolved.kind === 'search') {
				const searchUrl = `${resolve('/search')}?term=${encodeURIComponent(resolved.query)}`;
				await goto(searchUrl);
				return;
			}
			if (resolved.kind === 'youtubePlaylist') {
				if (resolved.results.length === 0) {
					pasteMessage = 'That playlist did not contain any playable tracks.';
					return;
				}
				queue.playNow(resolved.results[0]);
				for (const track of resolved.results.slice(1)) queue.add(track);
				pasteMessage = `Playing the first of ${resolved.results.length} playlist tracks.`;
			} else {
				queue.playNow(resolved.result);
				pasteMessage = `Playing ${resolved.result.name}.`;
			}
			pastedQuery = '';
		} catch (error) {
			pasteError = error instanceof AudioApiError ? error.message : 'Could not resolve that link. Please try again.';
		} finally {
			resolving = false;
		}
	}
</script>

<div class="page gap-10 p-4 sm:p-6 sm:pb-28">
	<section
		class="overflow-hidden rounded-panel border border-haze bg-surface-100 bg-[radial-gradient(120%_140%_at_8%_20%,color-mix(in_srgb,var(--color-primary-0)_26%,transparent),transparent_62%)]"
	>
		{#if hero}
			<div class="grid gap-5 p-4 sm:grid-cols-[11rem_1fr] sm:items-center sm:gap-6 sm:p-6 lg:grid-cols-[15rem_1fr] lg:gap-8 lg:p-8">
				<img
					src={hero.thumbnailUrl ?? '/empty.png'}
					alt=""
					class="aspect-square w-full max-w-32 rounded-row object-cover sm:max-w-44 lg:max-w-60"
					onerror={imageFallback}
				/>
				<div class="flex min-w-0 flex-col justify-center">
					<p class="eyebrow text-primary-500">The roll</p>
					<h1
						class="mt-2 font-display text-xl font-light leading-tight tracking-tight text-chalk sm:text-2xl lg:text-3xl"
					>
						{hero.name}
					</h1>
					<ArtistLink artist={hero.artist} class="mt-1 w-fit text-fog" />
					<p class="mt-2 flex flex-wrap items-center gap-2 font-mono text-[0.68rem] uppercase tracking-[0.13em] text-fog">
						<span>{heroDuration}{heroFormat ? ` · ${heroFormat === 'FLAC' ? 'FLAC available' : heroFormat}` : ''}</span>
						{#if variant}
							<!-- The lookup lands after the roll does, so this is the only thing that says a
							     prompt is waiting behind the buttons. Gold is the library, and an uncertain
							     match keeps the hairline in haze the way the prompt itself does. -->
							<span
								class="tag rounded-art border px-1.5 py-0.5 {variant.match === 'weak'
									? 'border-haze text-fog'
									: 'border-gold/45 text-gold'}">Alternative found</span
							>
						{/if}
					</p>
					{#if pending && variant}
						<!-- A fork in a row of buttons, not a dialog: no overlay, no dimmed page, no focus trap. -->
						<div
							class="prompt mt-4 max-w-md rounded-row border p-3 {variant.match === 'weak'
								? 'border-haze'
								: 'border-gold/45'}"
							role="group"
							aria-label="Choose which copy of {hero.name} to {verb.toLowerCase()}"
						>
							<p class="eyebrow {variant.match === 'weak' ? 'text-fog' : 'text-gold'}">{eyebrow}</p>
							<p class="mt-1.5 text-sm text-fog">
								{variantLine}{#if variant.match === 'variant' && rendition}
									· this one is <span class="text-ember">{rendition}</span>
								{/if}
							</p>
							<div class="mt-3 flex flex-wrap gap-2">
								<button
									bind:this={promptFirst}
									type="button"
									class="min-h-11 rounded-row bg-primary-600 px-3 py-2 text-sm font-semibold text-white"
									onkeydown={escapeCloses}
									onclick={() => choose(true)}
									>{verb} the {variant.match === 'variant' ? 'original' : 'library copy'}</button
								>
								<button
									type="button"
									class="min-h-11 rounded-row border border-haze px-3 py-2 text-sm font-semibold text-chalk hover:bg-surface-200"
									onkeydown={escapeCloses}
									onclick={() => choose(false)}
									>{verb} the {variant.match === 'variant' && rendition ? rendition : 'YouTube'} one</button
								>
							</div>
						</div>
					{:else}
						<div class="mt-4 flex flex-wrap gap-2">
							<!-- in a room nobody plays anything directly, so queueing is the only verb and it takes the accent -->
							{#if !session.inRoom}
								<button
									type="button"
									class="inline-flex min-h-11 items-center gap-2 rounded-row bg-primary-600 px-3 py-2 text-sm font-semibold text-white"
									onclick={(event) => press('play', event)}><Icon src={Play} mini size="16" /> Play</button
								>
							{/if}
							<button
								type="button"
								class="min-h-11 rounded-row px-3 py-2 text-sm font-semibold {session.inRoom
									? 'bg-primary-600 text-white'
									: 'border border-haze text-chalk hover:bg-surface-200'}"
								onclick={(event) => press('queue', event)}>Add to queue</button
							>
							<button
								type="button"
								class="inline-flex min-h-11 items-center gap-2 rounded-row border border-haze px-3 py-2 text-sm font-semibold text-chalk hover:bg-surface-200 disabled:opacity-60"
								onclick={rollAgain}
								disabled={rolling}
								><Icon src={ArrowPath} mini size="16" class={rolling ? 'animate-spin' : ''} /> Roll again</button
							>
						</div>
					{/if}
					<p class="sr-only" aria-live="polite">{pending && variant ? `${eyebrow}. ${variantLine}` : ''}</p>
					<!-- The odds and the outcome in one object: the seam is where you set the
					     split, the lit dot is the side this roll actually came from. -->
					<div class="mt-5 max-w-xs sm:max-w-sm">
						<div
							class="mb-2 flex items-center justify-between font-mono text-[0.62rem] uppercase tracking-[0.13em]"
						>
							<span class="flex items-center gap-1.5 {heroInLibrary ? 'text-gold' : 'text-fog'}">
								<span
									class="size-1.5 rounded-full bg-gold transition-opacity duration-300"
									class:opacity-0={!heroInLibrary}
								></span>
								Library {libraryPercent}%
							</span>
							<span class="flex items-center gap-1.5 {heroInLibrary ? 'text-fog' : 'text-ember'}">
								{youTubePercent}% YouTube
								<span
									class="size-1.5 rounded-full bg-ember transition-opacity duration-300"
									class:opacity-0={heroInLibrary}
								></span>
							</span>
						</div>
						<div
							class="group relative flex h-7 cursor-pointer touch-none items-center rounded-row outline-surface-300 focus-visible:outline-4"
							role="slider"
							tabindex="0"
							aria-label="Share of each roll drawn from your library, in percent"
							aria-valuemin="0"
							aria-valuemax="100"
							aria-valuenow={libraryPercent}
							aria-valuetext="{libraryPercent}% library, {youTubePercent}% YouTube"
							onfocusin={odds.enter}
							onfocusout={odds.leave}
							onpointerenter={odds.enter}
							onpointerleave={odds.leave}
							onpointerdown={odds.pointerDown}
							onpointermove={odds.pointerMove}
							onpointerup={odds.pointerUp}
							onpointercancel={odds.pointerUp}
							onkeydown={odds.keydown}
						>
							<div
								class="flex h-2 w-full overflow-hidden rounded-row transition-[height] duration-150 group-hover:h-3 group-focus-visible:h-3"
							>
								<div class="bg-gold transition-[width] duration-100" style:width={libraryPercent + '%'}></div>
								<div class="flex-1 bg-ember"></div>
							</div>
							<!-- ringed in the page ground so the seam stays legible against either side -->
							<div
								class="pointer-events-none absolute top-1/2 h-4 w-[3px] -translate-x-1/2 -translate-y-1/2 rounded-full bg-chalk shadow-[0_0_0_2px_var(--color-dark-0)] transition-[left,height] duration-100 group-hover:h-5 group-focus-visible:h-5"
								style:left={libraryPercent + '%'}
							></div>
						</div>
						<p class="sr-only">This roll came from {heroInLibrary ? 'the library' : 'YouTube'}.</p>
					</div>
				</div>
			</div>
		{:else}
			<p class="p-6 text-fog">The roll is resting for a moment. Try again shortly.</p>
		{/if}
	</section>

	<section class="rounded-panel border border-haze bg-surface-0/50 p-4 sm:p-5">
		<h2 class="eyebrow mb-3 flex items-center gap-2">
			<Icon src={Link} mini size="14" class="text-gold" /> Paste a link or ID
		</h2>
		<form
			class="flex flex-col gap-2 sm:flex-row"
			onsubmit={(event) => {
				event.preventDefault();
				resolvePaste();
			}}
		>
			<input
				bind:value={pastedQuery}
				class="min-w-0 flex-1 rounded-row border-haze bg-dark-0 text-chalk placeholder:text-fog focus:border-primary-0 focus:ring-primary-0"
				placeholder="YouTube link, playlist, or audio:// ID"
				aria-label="Paste a link or audio ID"
			/>
			<button
				type="submit"
				class="min-h-11 rounded-row bg-primary-600 px-4 py-2 text-sm font-semibold text-white disabled:opacity-60"
				disabled={resolving}>{resolving ? 'Resolving…' : 'Play'}</button
			>
		</form>
		{#if pasteError}<p class="mt-2 text-sm text-gold">{pasteError}</p>{/if}
		{#if pasteMessage}<p class="mt-2 text-sm text-fog">{pasteMessage}</p>{/if}
	</section>

	{#if artistSongs.length > 0}
		<section>
			<h2 class="eyebrow mb-3">More from this artist</h2>
			<div class="flex gap-4 overflow-x-auto pb-2">
				{#each artistSongs as song (song.id)}<Song {song} />{/each}
			</div>
		</section>
	{/if}
	{#if recentlyPlayed.length > 0}
		<section>
			<h2 class="eyebrow mb-3">Back where you left off</h2>
			<div class="flex gap-4 overflow-x-auto pb-2">
				{#each recentlyPlayed as song (song.id)}<Song {song} />{/each}
			</div>
		</section>
	{/if}

	<section>
		<h2 class="eyebrow mb-3">Artists in the library</h2>
		<div class="flex flex-wrap gap-2">
			{#each artists as [artist, count] (artist)}
				<a
					href={artistUrl(artist)}
					class="inline-flex min-h-9 items-center rounded-full border border-haze bg-surface-0 px-3 py-1 text-sm text-chalk transition-colors hover:border-gold hover:text-gold"
					>{artist}<span class="ml-1.5 font-mono text-[0.68rem] text-fog">{count}</span></a
				>
			{/each}
		</div>
	</section>

	<section>
		<div class="mb-3 flex items-center gap-3">
			<h2 class="eyebrow">(Curated) Picks</h2>
			<button
				type="button"
				class="inline-flex min-h-9 items-center gap-1.5 rounded-[5px] border border-haze px-2.5 py-1 text-xs font-semibold text-chalk hover:bg-surface-200 disabled:opacity-60"
				onclick={rollPicks}
				disabled={rollingPicks}
				><Icon src={ArrowPath} mini size="14" class={rollingPicks ? 'animate-spin' : ''} /> Roll again</button
			>
		</div>
		<div class="grid grid-flow-col-dense grid-rows-2 gap-4 overflow-x-auto p-2 sm:gap-6">
			{#each curated as song (song.id)}<Song {song} />{/each}
		</div>
	</section>
</div>

<style>
	/* The row's own hover timing. ponytail: opacity only — the prompt replaces a
	   button row of near-identical height, so an animated height buys nothing. The
	   water/ripple idea belongs to landing in the queue and is not spent twice. */
	.prompt,
	.tag {
		animation: reveal 150ms ease-out;
	}

	@keyframes reveal {
		from {
			opacity: 0;
			transform: translateY(-4px);
		}
	}

	@media (prefers-reduced-motion: reduce) {
		.prompt,
		.tag {
			animation: none;
		}
	}
</style>
