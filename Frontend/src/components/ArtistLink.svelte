<script lang="ts">
	import { resolve } from '$app/paths';
	import { splitArtists } from '$lib/artists';

	const { artist, class: className = '' }: { artist: string; class?: string } = $props();

	// One link per performer: the comma-joined credit is what the tag holds, but a click means
	// "this artist", not the whole line.
	const names = $derived(splitArtists(artist));

	// A literal ", " between the links would lose its space: Svelte trims the text node at the edge
	// of the block it sits in.
	const separator = ', ';
</script>

<span class={className}
	>{#each names as name, index (index)}{#if index > 0}{separator}{/if}<a
			href={`${resolve('/artist')}?term=${encodeURIComponent(name)}`}
			draggable="false"
			class="rounded-art underline-offset-4 hover:text-chalk hover:underline focus-visible:outline-2 focus-visible:outline-primary-200"
			>{name}</a
		>{/each}</span
>
