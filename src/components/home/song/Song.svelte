<script lang="ts">
	import type { SearchResult } from '$states/search.svelte';
	import { Icon, Plus } from 'svelte-hero-icons';
	const { song }: { song: SearchResult } = $props();
	import queue from '$states/queue.svelte';
	import ArtistLink from '$components/ArtistLink.svelte';

	let inLibrary = $derived(song.id.startsWith('audio://'));

	const onClick = () => {
		queue.add(song);
	};
</script>

<div class="relative flex flex-col gap-2 min-w-48 group">
	<img
		src={song.thumbnailUrl ?? '/empty.png'}
		alt=""
		class="size-48 rounded-art object-cover relative"
		onerror={(e: Event) => {
			const img = e.currentTarget as HTMLImageElement;
			if (img.src.endsWith('/empty.png')) return;
			img.src = '/empty.png';
		}}
	/>
	<!-- Solid fill, not an outline: it is the only thing that stays legible over
	     arbitrary artwork, so the label can stay small and still read. -->
	<span
		class="absolute left-1.5 top-1.5 rounded-art px-1 font-mono text-[0.55rem] font-bold leading-[1.5] tracking-tight text-dark-0 {inLibrary
			? 'bg-gold'
			: 'bg-ember'}">{inLibrary ? 'Local' : 'YouTube'}</span
	>

	<div class="grid">
		<span class="truncate text-sm font-medium text-chalk">{song.name}</span>
		<span class="truncate text-sm text-fog"><ArtistLink artist={song.artist} /></span>
	</div>

	<button
		onclick={onClick}
		class="absolute flex justify-center items-center size-8
	rounded-full cursor-pointer
	right-0 bottom-0 duration-150
	bg-primary-600 opacity-0 focus-visible:opacity-100 outline-0 group-hover:opacity-100"
	>
		<Icon src={Plus} mini size="20" color="white" />
	</button>
</div>
