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
	<div id="track-info" class="flex min-w-0 max-w-56 shrink items-center gap-2">
	<div class="flex min-w-0 flex-col">
		<span class="truncate text-right text-xs font-semibold text-chalk select-none">{current.name}</span>
		<span class="truncate text-right text-xs text-fog"><ArtistLink artist={current.artist} /></span>
	</div>
	<img
		src={thumbnail}
		alt=""
		class="hidden size-10 shrink-0 rounded-art object-cover md:block"
		onerror={(event: Event) => {
			const image = event.currentTarget as HTMLImageElement;
			if (!image.src.endsWith(empty)) image.src = empty;
		}}
	/>
</div>
{/if}
