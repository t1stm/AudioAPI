<script lang="ts">
	import { Beaker, Cloud, FolderOpen, Icon, MagnifyingGlass, User } from 'svelte-hero-icons';
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

<header
	class="box-border flex h-12 w-full shrink-0 justify-center bg-surface-0 px-3 py-1.5 micro:hidden sm:h-14 sm:px-4 sm:py-2"
>
	<div class="flex h-full w-full justify-between gap-2">
		<!-- The mark and the way into the library travel together: one flex child, so
		     justify-between distributes its space around the pair instead of pushing the
		     folder off towards the search field. -->
		<div class="flex shrink-0 items-center gap-2 sm:mr-2">
			<a
				href={resolve('/')}
				class="flex items-center gap-1.5 rounded-row outline-none focus-visible:ring-2 focus-visible:ring-primary-500"
			>
				<span class="hidden font-display text-lg font-extralight tracking-tight text-chalk sm:inline"
					>music<b class="font-medium text-primary-500">rain</b></span
				>
				<Icon src={Cloud} solid class="size-6 shrink-0 text-primary-500" />
				{#if isAlpha}
					<Icon src={Beaker} micro class="mt-auto mb-1 hidden size-3.5 text-fog sm:block" />
				{/if}
			</a>

			<!-- Gold is the library everywhere in this app, so the way into it is gold on hover.
			     Hidden below sm: the header is a logo, a search field and a face at 320px, and the
			     home page carries the same link. -->
			<a
				href={resolve('/browse')}
				aria-label="Browse the library by folder"
				class="hidden size-10 shrink-0 items-center justify-center rounded-row border border-haze text-fog outline-none hover:border-gold hover:text-gold focus-visible:ring-2 focus-visible:ring-primary-500 sm:flex"
				class:border-gold={page.url.pathname.startsWith('/browse')}
				class:text-gold={page.url.pathname.startsWith('/browse')}
			>
				<Icon src={FolderOpen} micro size="20" />
			</a>
		</div>

		<form class="flex gap-2 w-full max-w-lg" action="/search">
			<input
				type="text"
				name="term"
				bind:value={searchTerm}
				placeholder="Search"
				class="rounded-row border border-haze bg-dark-0 w-full text-chalk placeholder:text-fog ring-primary-0 focus:border-primary-0 focus-visible:ring-2"
			/>
			<!-- the form already submits on Enter; the button is a desktop affordance -->
			<button
				class="hidden size-10 min-w-10 cursor-pointer items-center justify-center rounded-row bg-primary-600 sm:flex"
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
				class="flex size-9 shrink-0 cursor-pointer items-center justify-center overflow-hidden rounded-full bg-primary-600 outline-none focus-visible:ring-2 focus-visible:ring-primary-200 sm:size-10"
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
					class="absolute right-0 z-30 mt-2 w-[min(18rem,calc(100vw-1.5rem))] rounded-panel border border-haze bg-surface-100 p-3 text-left"
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
