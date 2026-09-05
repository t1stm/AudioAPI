<script lang="ts">
	import current from '$states/current.svelte';
	import ArtistLink from '$components/ArtistLink.svelte';
	import { sourceOf } from '$lib/source';


	// static/ is served at the root; '/static/empty.png' 404s.
	const empty = '/empty.png';
	let thumbnail = $derived(current.thumbnail?.length > 0 ? current.thumbnail : empty);
	let source = $derived(sourceOf(current.id));

  $effect(() => {
    navigator.mediaSession.metadata = new MediaMetadata({
      title: current.name,
      artist: current.artist,
      album: current.album,
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
			title={`From ${source.name}`}
			onerror={(event: Event) => {
				const image = event.currentTarget as HTMLImageElement;
				if (!image.src.endsWith(empty)) image.src = empty;
			}}
		/>
		<div class="flex min-w-0 flex-col">
			<div class="flex min-w-0 items-center gap-1.5 sm:flex-row-reverse">
				<span class="truncate text-xs font-semibold text-chalk select-none">{current.name}</span>
				<span
					class="shrink-0 rounded-art px-1 font-mono text-[0.5rem] font-bold uppercase leading-[1.6] tracking-tight {source.badge}"
					>{source.name}</span
				>
			</div>
			<span class="truncate text-xs text-fog sm:text-right"
				><ArtistLink artist={current.artist} /></span
			>
			<!-- The tag, or nothing: an "Unknown album" line is a row of dead pixels in a
			     53px bar. Only the full player and micro have the room to show it — see
			     `#track-album` in app.css. -->
			{#if current.album}
				<span id="track-album" class="eyebrow truncate">{current.album}</span>
			{/if}
		</div>
	</div>
{/if}
