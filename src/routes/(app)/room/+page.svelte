<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { resolve } from '$app/paths';
	import ArtistLink from '$components/ArtistLink.svelte';
	import current from '$states/current.svelte';
	import queue from '$states/queue.svelte';
	import rooms from '$states/rooms.svelte';
	import session from '$states/session.svelte';
	import user from '$states/user.svelte';
	import { roomLabel } from '$requests/rooms';

	let roomId = $derived(page.url.searchParams.get('id') ?? '');
	let draftName = $state('');
	let listed = $derived(rooms.list.find((room) => room.roomID === roomId));
	let heading = $derived(session.name || (listed ? roomLabel(listed) : 'this room'));
	let upNext = $derived(queue.items.slice(queue.currentIndex + 1));

	onMount(() => {
		// only for this page's own heading — the layout holds the feed for as long
		// as the session lives. Leaving the route never leaves the room: that is
		// what Leave on the session strip is for.
		rooms.connect();
		return () => rooms.disconnect();
	});

	$effect(() => {
		if (!roomId || user.username === null) return;
		if (session.roomId === roomId && session.joinedAs === user.username) return;
		session.connect(roomId, user.username);
	});

	function imageFallback(event: Event) {
		const image = event.currentTarget as HTMLImageElement;
		if (!image.src.endsWith('/empty.png')) image.src = '/empty.png';
	}
</script>

<svelte:head><title>{heading} · musicrain</title></svelte:head>

<div class="page gap-6 p-4 pb-28 sm:p-8 sm:pb-32">
	{#if !roomId}
		<p class="text-sm text-fog">
			No room in the link. <a class="text-primary-500 underline-offset-4 hover:underline" href={resolve('/rooms')}>Browse rooms</a>
		</p>
	{:else if session.gone}
		<section class="max-w-md">
			<h1 class="font-display mb-2 text-xl font-extralight">That room is gone.</h1>
			<p class="text-sm text-fog">Rooms disappear when the server restarts.</p>
			<a
				href={resolve('/rooms')}
				class="mt-4 inline-block rounded-row bg-primary-600 px-3 py-2 text-sm font-semibold text-white hover:bg-primary-0"
			>
				Browse rooms
			</a>
		</section>
	{:else if user.username === null}
		<!-- the name is a precondition of joining, so it belongs on the room -->
		<section class="max-w-md">
			<h1 class="font-display mb-2 text-xl font-extralight">Pick a name before you join</h1>
			<p class="text-sm text-fog">
				Everyone in the room sees it on your messages. You can change it later.
			</p>
			<form
				class="mt-4 flex flex-col gap-3"
				onsubmit={(event) => {
					event.preventDefault();
					user.choose(draftName);
				}}
			>
				<input
					type="text"
					bind:value={draftName}
					maxlength="60"
					aria-label="Your name"
					class="rounded-row border border-haze bg-dark-0 text-chalk placeholder:text-fog ring-primary-0 focus:border-primary-0 focus-visible:ring-2"
				/>
				<div class="flex items-center gap-4">
					<button
						type="submit"
						disabled={!draftName.trim()}
						class="rounded-row bg-primary-600 px-3 py-2 text-sm font-semibold text-white hover:bg-primary-0 disabled:opacity-60"
					>
						Join {heading}
					</button>
					<button
						type="button"
						class="text-sm text-fog underline-offset-4 hover:text-chalk hover:underline"
						onclick={() => user.choose('')}
					>
						Join without a name
					</button>
				</div>
			</form>
		</section>
	{:else}
		<section>
			<h2 class="eyebrow mb-3">Now playing</h2>
			{#if current.name}
				<div class="flex items-center gap-4">
					<img
						src={current.thumbnail || '/empty.png'}
						alt=""
						class="size-20 rounded-art object-cover"
						onerror={imageFallback}
					/>
					<div class="min-w-0">
						<p class="font-display truncate text-lg font-normal">{current.name}</p>
						<p class="truncate text-sm text-fog"><ArtistLink artist={current.artist} /></p>
					</div>
				</div>
			{:else}
				<p class="text-sm text-fog">
					Nothing playing. Add something from
					<a class="text-primary-500 underline-offset-4 hover:underline" href={resolve('/search')}>search</a>
					— everyone in the room hears it.
				</p>
			{/if}
		</section>

		<section>
			<h2 class="eyebrow mb-2">Up next · {upNext.length}</h2>
			{#if upNext.length === 0}
				<p class="text-sm text-fog">Nothing queued after this track.</p>
			{:else}
				<ul class="divide-y divide-haze">
					{#each upNext as item, offset (item.id + offset)}
						<li class="flex items-center gap-3 py-2">
							<span class="w-5 text-right font-mono text-[0.68rem] text-fog">{offset + 1}</span>
							<img
								src={item.thumbnailUrl ?? '/empty.png'}
								alt=""
								class="size-9 rounded-art object-cover"
								onerror={imageFallback}
							/>
							<div class="min-w-0 flex-1">
								<p class="truncate text-sm">{item.name}</p>
								<p class="truncate text-xs text-fog"><ArtistLink artist={item.artist} /></p>
							</div>
							<button
								type="button"
								class="rounded-row border border-haze px-2 py-1 text-xs hover:bg-surface-200"
								onclick={() => queue.playIndex(queue.currentIndex + offset + 1)}
							>
								Play
							</button>
						</li>
					{/each}
				</ul>
			{/if}
		</section>
	{/if}
</div>
