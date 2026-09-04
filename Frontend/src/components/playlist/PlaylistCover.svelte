<script lang="ts">
	import { coverFor } from '$states/playlists.svelte';
	import type { PlaylistSummary } from '$requests/playlists';

	// the three-way rule lives in `coverFor`; this is the only component that draws it
	let {
		playlist,
		alt = '',
		class: className = ''
	}: { playlist: PlaylistSummary; alt?: string; class?: string } = $props();

	let source = $derived(coverFor(playlist));

	function fallback(event: Event) {
		const image = event.currentTarget as HTMLImageElement;
		if (!image.src.endsWith('/empty.png')) image.src = '/empty.png';
	}
</script>

<img src={source} {alt} class={className} onerror={fallback} />
