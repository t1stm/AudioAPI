<script lang="ts">
	import { Beaker, Cloud, Icon, MagnifyingGlass, User } from 'svelte-hero-icons';
	import { page } from '$app/state';
	import { resolve } from '$app/paths';

	const isAlpha = true;
	let searchTerm = $derived(page.url.searchParams.get('term') ?? '');
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

		<button
			type="button"
			aria-label="Open profile"
			class="flex w-10 h-10 rounded-full bg-primary-600 items-center justify-center cursor-pointer outline-none focus-visible:ring-2 focus-visible:ring-primary-200"
		>
			<Icon src={User} micro size="24" color="white" />
		</button>
	</div>
</header>
