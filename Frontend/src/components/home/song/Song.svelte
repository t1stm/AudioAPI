<script lang="ts">
	import type { SearchResult } from '$states/search.svelte';
	import { Check, Icon, Plus } from 'svelte-hero-icons';
	const { song }: { song: SearchResult } = $props();
	import queue from '$states/queue.svelte';
	import ArtistLink from '$components/ArtistLink.svelte';

	let inLibrary = $derived(song.id.startsWith('audio://'));

	// The queue is a dock away and the badge that counts it is at the foot of the
	// screen — on a phone the thumb needs to hear it here, where it tapped.
	let added = $state(false);
	let settle: ReturnType<typeof setTimeout>;

	const onClick = () => {
		queue.add(song);
		added = true;
		clearTimeout(settle);
		settle = setTimeout(() => (added = false), 1100);
	};
</script>

<div class="group relative flex min-w-36 flex-col gap-2 sm:min-w-48">
	<img
		src={song.thumbnailUrl ?? '/empty.png'}
		alt=""
		class="relative size-36 rounded-art object-cover sm:size-48"
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
		aria-label={added ? 'Added to queue' : `Add ${song.name} to queue`}
		class="absolute flex justify-center items-center size-8
	rounded-full cursor-pointer
	right-0 bottom-0 duration-150
	outline-0 opacity-100 sm:opacity-0 focus-visible:opacity-100 group-hover:opacity-100
	{added ? 'added bg-primary-0' : 'bg-primary-600'}"
	>
		<Icon src={added ? Check : Plus} mini size="20" color="white" />
	</button>
</div>

<style>
	/* The app's one motion idea is water. A track does not pop into the queue, it
	   lands in it — the ring is the ripple the drop leaves behind. */
	@keyframes ripple {
		from {
			box-shadow: 0 0 0 0 color-mix(in srgb, var(--color-primary-200) 70%, transparent);
		}
		to {
			box-shadow: 0 0 0 12px transparent;
		}
	}

	.added {
		animation: ripple 700ms ease-out;
	}

	@media (prefers-reduced-motion: reduce) {
		.added {
			animation: none;
		}
	}
</style>
