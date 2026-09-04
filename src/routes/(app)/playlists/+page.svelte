<script lang="ts">
	import type { PageData } from './$types';
	import account from '$states/account.svelte';
	import playlists from '$states/playlists.svelte';
	import PlaylistCard from '$components/playlist/PlaylistCard.svelte';

	const { data }: { data: PageData } = $props();

	// the load function fetched the public list once; the store owns it from here, so a
	// playlist made public from your own page shows up without a reload
	playlists.shared = data.shared;

	// `mine` needs the token, which lands after the layout's onMount reads localStorage
	$effect(() => {
		if (account.token) playlists.loadMine();
	});

	let mine = $derived(playlists.mine);
	let groups = $derived([
		{ label: 'Public', list: mine.filter((p) => p.isPublic) },
		{ label: 'Private', list: mine.filter((p) => !p.isPublic) }
	]);
</script>

<svelte:head><title>Playlists · musicrain</title></svelte:head>

<div class="page mx-auto w-full max-w-5xl gap-6 p-4 sm:gap-9 sm:p-6 sm:pb-28">
	<div>
		<p class="eyebrow text-primary-500">Yours and everybody's</p>
		<h1
			class="mt-2 font-display text-lg font-light leading-tight tracking-tight text-chalk sm:text-2xl"
		>
			Playlists
		</h1>
		<p class="mt-2 max-w-lg text-fog">
			What people cut out of the library. Save one from the queue, then send it or keep it.
		</p>
	</div>

	{#if playlists.error}
		<p class="text-sm text-ember">{playlists.error}</p>
	{/if}

	<section class="flex flex-col gap-4">
		<h2 class="eyebrow flex items-center gap-3">
			Yours
			<span class="h-px flex-1 bg-haze"></span>
		</h2>

		{#if !account.signedIn}
			<p class="max-w-lg text-fog">
				Sign in to keep playlists. Open the account panel from your avatar in the header to sign
				in or create an account.
			</p>
		{:else if mine.length === 0}
			<p class="max-w-lg text-fog">You have no playlists. Queue a few tracks and save them.</p>
		{:else}
			{#each groups as group (group.label)}
				{#if group.list.length > 0}
					<section class="flex flex-col gap-2">
						<h3 class="eyebrow">{group.label} · {group.list.length}</h3>
						<div
							class="grid grid-cols-2 gap-3 sm:grid-cols-[repeat(auto-fill,minmax(9.5rem,1fr))]"
						>
							{#each group.list as playlist (playlist.id)}
								<PlaylistCard {playlist} />
							{/each}
						</div>
					</section>
				{/if}
			{/each}
		{/if}
	</section>

	<section class="flex flex-col gap-2">
		<h2 class="eyebrow flex items-center gap-3">
			From everybody else · {playlists.others.length}
			<span class="h-px flex-1 bg-haze"></span>
		</h2>
		{#if playlists.others.length === 0}
			<p class="max-w-lg text-fog">
				{#if playlists.shared.length > 0}
					Nobody else has shared a playlist yet.
				{:else}
					No public playlists yet. Make one from your queue and share it.
				{/if}
			</p>
		{:else}
			<div class="grid grid-cols-2 gap-3 sm:grid-cols-[repeat(auto-fill,minmax(9.5rem,1fr))]">
				{#each playlists.others as playlist (playlist.id)}
					<PlaylistCard {playlist} />
				{/each}
			</div>
		{/if}
	</section>
</div>
