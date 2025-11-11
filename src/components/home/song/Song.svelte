<script lang="ts">
	import type { SearchResult } from '$states/search.svelte';
	import { Icon, Plus } from 'svelte-hero-icons';
	const { song }: { song: SearchResult } = $props();
	import queue from '$states/queue.svelte';

	const onClick = () => {
		queue.add(song);
	};
</script>

<div class="relative flex flex-col gap-2 min-w-48 group">
	<img
		src={song.thumbnailUrl ?? '/static/empty.png'}
		alt=""
		class="size-48 rounded-lg object-cover relative"
	/>
	<div class="grid">
		<span class="text-primary-500 text-sm font-bold truncate">{song.name}</span>
		<span class="text-surface-500 text-sm">{song.artist}</span>
	</div>

	<button
		onclick={onClick}
		class="absolute flex justify-center items-center size-8
	rounded-full cursor-pointer
	right-0 bottom-0 duration-150
	bg-primary-0 opacity-0 focus-visible:opacity-100 outline-0 group-hover:opacity-100"
	>
		<Icon src={Plus} mini size="20" color="white" />
	</button>
</div>
