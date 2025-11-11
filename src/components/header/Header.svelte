<script>
	import { Beaker, Cloud, Icon, MagnifyingGlass, User } from 'svelte-hero-icons';
  import { resolve } from "$app/paths"

	const isAlpha = true;
</script>

<header class="box-border bg-surface-0 w-full h-14 py-2 px-4 flex justify-center">
	<div class="flex justify-between w-full h-full">
		<a href={resolve('/')} class="flex items-center cursor-pointer">
			<span class="text-white text-xs">music<b class="text-lg">rain</b></span>
			<Icon src={Cloud} solid color="white" class="size-10" />
			{#if isAlpha}
				<Icon src={Beaker} micro class="size-4 text-primary-0 mt-auto" />
			{/if}
		</a>

		<form class="flex gap-2 w-full max-w-lg" action="/search">
			<input
				type="text"
				name="term"
				placeholder="Search"
				class="rounded-md border-0 bg-dark-0 w-full text-white ring-primary-0 focus-visible:ring-2"
			/>
			<button
				class="cursor-pointer min-w-10 size-10 flex items-center justify-center rounded-lg bg-primary-0"
				type="submit"
			>
				<Icon src={MagnifyingGlass} micro color="white" size="24" />
			</button>
		</form>

		<div
			class="flex w-10 h-10 rounded-full bg-primary-0 items-center justify-center cursor-pointer"
		>
			<Icon src={User} micro size="24" color="white" />
		</div>
	</div>
</header>
