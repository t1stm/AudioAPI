<script lang="ts">
	import current from '$states/current.svelte';
	import ArtistLink from '$components/ArtistLink.svelte';


	// static/ is served at the root; '/static/empty.png' 404s.
	const empty = '/empty.png';
	let thumbnail = $derived(current.thumbnail?.length > 0 ? current.thumbnail : empty);

  $effect(() => {
    navigator.mediaSession.metadata = new MediaMetadata({
      title: current.name,
      artist: current.artist,
      artwork: [{ src: thumbnail }]
    })
  })
</script>

{#if current.name}
	<div
		id="track-info"
		class="flex min-w-0 flex-1 shrink items-center gap-2 sm:order-2 sm:max-w-56 sm:flex-none sm:flex-row-reverse"
	>
		<img
			src={thumbnail}
			alt=""
			class="size-10 shrink-0 rounded-art object-cover"
			onerror={(event: Event) => {
				const image = event.currentTarget as HTMLImageElement;
				if (!image.src.endsWith(empty)) image.src = empty;
			}}
		/>
		<div class="flex min-w-0 flex-col">
			<span class="truncate text-xs font-semibold text-chalk select-none sm:text-right"
				>{current.name}</span
			>
			<span class="truncate text-xs text-fog sm:text-right"
				><ArtistLink artist={current.artist} /></span
			>
		</div>
	</div>
{/if}
