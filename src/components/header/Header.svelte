<script lang="ts">
	import { Beaker, Cloud, Icon, MagnifyingGlass, User } from 'svelte-hero-icons';
	import { page } from '$app/state';
	import { resolve } from '$app/paths';
	import session from '$states/session.svelte';
	import user from '$states/user.svelte';

	const isAlpha = true;
	let searchTerm = $derived(page.url.searchParams.get('term') ?? '');
	let editingName = $state(false);
	let draftName = $state('');

	function openName() {
		draftName = user.username ?? '';
		editingName = !editingName;
	}

	function saveName(event: SubmitEvent) {
		event.preventDefault();
		user.choose(draftName);
		editingName = false;
	}
</script>

<header class="box-border bg-surface-0 w-full h-14 py-2 px-4 flex justify-center">
	<div class="flex justify-between w-full h-full">
		<a
			href={resolve('/')}
			class="flex items-center gap-1.5 rounded-row outline-none focus-visible:ring-2 focus-visible:ring-primary-500"
		>
			<span class="font-display text-lg font-extralight tracking-tight text-chalk"
				>music<b class="font-medium text-primary-500">rain</b></span
			>
			<Icon src={Cloud} solid class="size-6 text-primary-500" />
			{#if isAlpha}
				<Icon src={Beaker} micro class="size-3.5 text-fog mt-auto mb-1" />
			{/if}
		</a>

		<form class="flex gap-2 w-full max-w-lg" action="/search">
			<input
				type="text"
				name="term"
				bind:value={searchTerm}
				placeholder="Search"
				class="rounded-row border border-haze bg-dark-0 w-full text-chalk placeholder:text-fog ring-primary-0 focus:border-primary-0 focus-visible:ring-2"
			/>
			<button
				class="cursor-pointer min-w-10 size-10 flex items-center justify-center rounded-row bg-primary-600"
				type="submit"
			>
				<Icon src={MagnifyingGlass} micro color="white" size="24" />
			</button>
		</form>

		<div class="relative shrink-0">
			<button
				type="button"
				aria-label={user.username ? `You are ${user.username}` : 'Set your name'}
				aria-expanded={editingName}
				class="flex w-10 h-10 overflow-hidden rounded-full bg-primary-600 items-center justify-center cursor-pointer outline-none focus-visible:ring-2 focus-visible:ring-primary-200"
				onclick={openName}
			>
				{#if user.avatarUrl}
					<!-- a blocked or dead CDN URL drops back to the icon -->
					<img
						src={user.avatarUrl}
						alt=""
						class="size-full object-cover"
						onerror={() => (user.avatarUrl = null)}
					/>
				{:else}
					<Icon src={User} micro size="24" color="white" />
				{/if}
			</button>

			{#if editingName}
				<form
					class="absolute right-0 z-30 mt-2 w-72 rounded-panel border border-haze bg-surface-100 p-3 text-left"
					onsubmit={saveName}
				>
					<h2 class="eyebrow mb-2">Your name</h2>
					<input
						type="text"
						bind:value={draftName}
						maxlength="60"
						placeholder="Anonymous"
						aria-label="Your name"
						class="rounded-row border border-haze bg-dark-0 w-full text-sm text-chalk placeholder:text-fog ring-primary-0 focus:border-primary-0 focus-visible:ring-2"
					/>
					{#if session.inRoom}
						<!-- the server applies a name only when a connection registers -->
						<p class="mt-2 text-xs text-fog">
							Renaming reconnects you. The room sees you leave and come back.
						</p>
					{/if}
					<button
						type="submit"
						class="mt-3 w-full rounded-row bg-primary-600 px-3 py-1.5 text-sm font-semibold text-white hover:bg-primary-0"
					>
						Save name
					</button>
				</form>
			{/if}
		</div>
	</div>
</header>
