<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { resolve } from '$app/paths';
	import rooms from '$states/rooms.svelte';
	import session from '$states/session.svelte';
	import { createRoom, isUnnamed, roomLabel } from '$requests/rooms';

	let filter = $state('');
	let newName = $state('');
	let starting = $state(false);
	let startError = $state('');

	let term = $derived(filter.trim().toLowerCase());
	let matched = $derived(
		term
			? rooms.list.filter(
					(room) =>
						roomLabel(room).toLowerCase().includes(term) ||
						room.description.toLowerCase().includes(term)
				)
			: rooms.list
	);
	let named = $derived(matched.filter((room) => !isUnnamed(room)));
	let unnamed = $derived(matched.filter(isUnnamed));

	onMount(() => {
		rooms.connect();
		return () => rooms.disconnect();
	});

	async function startRoom() {
		starting = true;
		startError = '';
		try {
			const room = await createRoom();
			// CreateRoom takes no body, so the name is applied over the session socket
			// as soon as it opens
			if (newName.trim()) session.prime([`updateroom name ${newName.trim()}`]);
			await goto(`${resolve('/room')}?id=${room.roomID}`);
		} catch (error) {
			startError = error instanceof Error ? error.message : 'Could not start a room.';
			starting = false;
		}
	}
</script>

<svelte:head><title>Rooms · musicrain</title></svelte:head>

<div class="page gap-6 p-4 sm:gap-8 sm:p-8 sm:pb-32">
	<header class="flex items-baseline justify-between gap-4">
		<h1 class="font-display text-xl font-extralight tracking-tight sm:text-2xl">Rooms</h1>
		<span class="eyebrow" class:text-primary-500={rooms.connected}>
			{rooms.connected ? 'live' : 'connecting'}
		</span>
	</header>

	<section class="rounded-panel border border-haze bg-surface-100 p-4">
		<h2 class="eyebrow mb-3">Start a room</h2>
		<form
			class="flex flex-col gap-2 sm:flex-row"
			onsubmit={(event) => {
				event.preventDefault();
				startRoom();
			}}
		>
			<input
				type="text"
				bind:value={newName}
				maxlength="120"
				placeholder="Name it (optional)"
				aria-label="Room name"
				class="rounded-row border border-haze bg-dark-0 w-full text-chalk placeholder:text-fog ring-primary-0 focus:border-primary-0 focus-visible:ring-2"
			/>
			<button
				type="submit"
				disabled={starting}
				class="min-h-11 shrink-0 rounded-row bg-primary-600 px-4 py-2 text-sm font-semibold text-white hover:bg-primary-0 focus-visible:outline-2 focus-visible:outline-primary-200 disabled:opacity-60"
			>
				{starting ? 'Starting…' : 'Start a room'}
			</button>
		</form>
		{#if startError}<p class="mt-2 text-sm text-ember">{startError}</p>{/if}
	</section>

	<input
		type="search"
		bind:value={filter}
		placeholder="Filter rooms"
		aria-label="Filter rooms"
		class="rounded-row border border-haze bg-dark-0 max-w-md text-chalk placeholder:text-fog ring-primary-0 focus:border-primary-0 focus-visible:ring-2"
	/>

	{#if rooms.list.length === 0}
		<p class="text-sm text-fog">No rooms yet. Start one and send the link.</p>
	{:else if matched.length === 0}
		<p class="text-sm text-fog">
			No room matches <b class="text-chalk">{filter.trim()}</b>. Start one with that name instead.
		</p>
	{/if}

	{#each [{ label: 'Named', list: named, unnamed: false }, { label: 'Never named', list: unnamed, unnamed: true }] as group (group.label)}
		{#if group.list.length > 0}
			<section>
				<h2 class="eyebrow mb-1 flex items-center gap-3">
					<span>{group.label} · {group.list.length}</span>
					<span class="h-px flex-1 bg-haze"></span>
				</h2>
				{#if group.unnamed}
					<!-- a room whose name is still its own GUID has never been renamed,
					     which is the only occupancy-shaped fact the payload carries -->
					<p class="mb-2 text-xs text-fog">
						These have probably been empty since they were made.
					</p>
				{/if}
				<ul class="divide-y divide-haze">
					{#each group.list as room (room.roomID)}
						<li class="flex items-center gap-3 py-3">
							<div class="min-w-0 flex-1">
								<p class="truncate text-sm font-semibold" class:text-fog={group.unnamed}>
									{group.unnamed ? 'Unnamed room' : roomLabel(room)}
								</p>
								{#if room.description.trim()}
									<p class="truncate text-xs text-fog">{room.description.trim()}</p>
								{/if}
							</div>
							<span class="hidden font-mono text-[0.68rem] text-gold sm:block"
								>{room.roomID.slice(0, 8)}</span
							>
							<a
								href={`${resolve('/room')}?id=${room.roomID}`}
								class="flex min-h-11 shrink-0 items-center rounded-row border border-haze px-3 text-xs font-semibold hover:bg-surface-200 focus-visible:outline-2 focus-visible:outline-primary-200"
							>
								Join
							</a>
						</li>
					{/each}
				</ul>
			</section>
		{/if}
	{/each}
</div>
