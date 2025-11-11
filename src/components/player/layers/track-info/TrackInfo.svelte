<script lang="ts">
	import current from '$states/current.svelte';
	import empty from '/static/empty.png';

	let thumbnail = $derived(current.thumbnail?.length > 0 ? current.thumbnail : empty);

  $effect(() => {
    navigator.mediaSession.metadata = new MediaMetadata({
      title: current.name,
      artist: current.artist,
      artwork: [{ src: thumbnail }]
    })
  })
</script>

<div id="track-info" class="flex items-center gap-1.5">
	<div class="flex flex-col">
		<span class="text-primary-500 font-bold text-xs text-right select-none">{current.name}</span>
		<span class="text-surface-500 text-xs text-right select-none">{current.artist}</span>
	</div>
	<img
		src={thumbnail}
		alt="Current Song's Album Cover"
		class="size-10 object-contain rounded-md hidden md:flex"
	/>
</div>
