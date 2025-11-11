<script lang="ts">
	import queue from '$states/queue.svelte';
	let items = $derived(queue.items);
</script>

<div
	class="bg-surface-0/60 backdrop-blur-lg p-4 box-border text-white flex flex-col gap-2 rounded-lg absolute bottom-0 right-0 -translate-y-16 w-full max-w-md h-128 overflow-y-auto"
>
	<span class="text-xl font-bold">Queue:</span>
	<ul class="grid gap-2">
		{#each items as item, index (index)}
			<li class="flex gap-2 max-h-32 items-center relative">
				<span
					class="absolute left-0 top-0 text-xs font-bold select-none bg-primary-0 rounded-full p-0.5 translate-x-1 translate-y-1"
					>#{index + 1}</span
				>
				<img src={item.thumbnailUrl} alt="" class="size-16 rounded-lg" />
				<div class="grid gap-1 h-fit">
					<span class="text-primary-500 font-bold select-none">{item.name}</span>
					<span class="text-surface-500 select-none">{item.artist}</span>
				</div>
			</li>
		{/each}
	</ul>
</div>
