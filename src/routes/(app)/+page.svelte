<script lang="ts">
	import { goto } from '$app/navigation';
	import { resolve } from '$app/paths';
	import { onMount } from 'svelte';
	import type { PageData } from './$types';
	import type { SearchResult } from '$states/search.svelte';
	import queue from '$states/queue.svelte';
	import session from '$states/session.svelte';
	import { getRecentlyPlayed } from '$lib/recentlyPlayed';
	import { AudioApiError, findQueryType, getArtistLocal, getRandomSongs } from '$requests/songs';
	import { convertTimeSpanStringToSeconds, getTimeString } from '$lib';
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
	let heroFormat = $derived(
		(heroInLibrary && hero?.contentUrl?.split('?')[0].match(/\.(\w{2,4})$/)?.[1].toUpperCase()) || ''
	);

	onMount(() => {
		recentlyPlayed = getRecentlyPlayed();
	});

	function imageFallback(event: Event) {
		const image = event.currentTarget as HTMLImageElement;
		if (!image.src.endsWith('/empty.png')) image.src = '/empty.png';
	}

	async function rollAgain() {
		if (rolling) return;
		rolling = true;
		try {
			const nextHero = (await getRandomSongs(fetch, 1))[0] ?? null;
			hero = nextHero;
			artistSongs = nextHero ? await getArtistLocal(nextHero.artist, fetch) : [];
		} finally {
			rolling = false;
		}
	}

	async function rollPicks() {
		if (rollingPicks) return;
		rollingPicks = true;
		try {
			curated = await getRandomSongs(fetch);
		} finally {
			rollingPicks = false;
		}
	}

	function playHero() {
		if (hero) queue.playNow(hero);
	}

	function queueHero() {
		if (hero) queue.add(hero);
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

<div class="page gap-10 p-4 pb-36 sm:pb-28 sm:p-6">
	<section
		class="overflow-hidden rounded-panel border border-haze bg-surface-100 bg-[radial-gradient(120%_140%_at_8%_20%,color-mix(in_srgb,var(--color-primary-0)_26%,transparent),transparent_62%)]"
	>
		{#if hero}
			<div class="grid gap-6 p-5 sm:grid-cols-[11rem_1fr] sm:items-center sm:p-6 lg:grid-cols-[15rem_1fr] lg:gap-8 lg:p-8">
				<img
					src={hero.thumbnailUrl ?? '/empty.png'}
					alt=""
					class="aspect-square w-full max-w-44 rounded-row object-cover lg:max-w-60"
					onerror={imageFallback}
				/>
				<div class="flex min-w-0 flex-col justify-center">
					<p class="eyebrow text-primary-500">The roll</p>
					<h1
						class="mt-2 font-display text-2xl font-light leading-tight tracking-tight text-chalk sm:text-3xl"
					>
						{hero.name}
					</h1>
					<ArtistLink artist={hero.artist} class="mt-1 w-fit text-fog" />
					<div class="mt-4 flex flex-wrap gap-2">
						<!-- in a room nobody plays anything directly, so queueing is the only verb and it takes the accent -->
						{#if !session.inRoom}
							<button
								type="button"
								class="inline-flex items-center gap-2 rounded-row bg-primary-600 px-3 py-2 text-sm font-semibold text-white"
								onclick={playHero}><Icon src={Play} mini size="16" /> Play</button
							>
						{/if}
						<button
							type="button"
							class="rounded-row px-3 py-2 text-sm font-semibold {session.inRoom
								? 'bg-primary-600 text-white'
								: 'border border-haze text-chalk hover:bg-surface-200'}"
							onclick={queueHero}>Add to queue</button
						>
						<button
							type="button"
							class="inline-flex items-center gap-2 rounded-row border border-haze px-3 py-2 text-sm font-semibold text-chalk hover:bg-surface-200 disabled:opacity-60"
							onclick={rollAgain}
							disabled={rolling}
							><Icon src={ArrowPath} mini size="16" class={rolling ? 'animate-spin' : ''} /> Roll again</button
						>
					</div>
					<p class="mt-4 font-mono text-[0.68rem] uppercase tracking-[0.13em] text-fog">
						{heroDuration}{heroInLibrary ? ' · Library' : ''}{heroFormat
							? ` · ${heroFormat === 'FLAC' ? 'FLAC available' : heroFormat}`
							: ''}
					</p>
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
				class="rounded-row bg-primary-600 px-4 py-2 text-sm font-semibold text-white disabled:opacity-60"
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
					class="rounded-full border border-haze bg-surface-0 px-3 py-1 text-sm text-chalk transition-colors hover:border-gold hover:text-gold"
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
				class="inline-flex items-center gap-1.5 rounded-[5px] border border-haze px-2.5 py-1 text-xs font-semibold text-chalk hover:bg-surface-200 disabled:opacity-60"
				onclick={rollPicks}
				disabled={rollingPicks}
				><Icon src={ArrowPath} mini size="14" class={rollingPicks ? 'animate-spin' : ''} /> Roll again</button
			>
		</div>
		<div class="grid grid-flow-col-dense grid-rows-2 gap-6 overflow-x-auto p-2">
			{#each curated as song (song.id)}<Song {song} />{/each}
		</div>
	</section>
</div>
