<script lang="ts">
	import { resolve } from '$app/paths';

	const { artist, class: className = '' }: { artist: string; class?: string } = $props();
</script>

<a
	href={`${resolve('/artist')}?term=${encodeURIComponent(artist)}`}
	draggable="false"
	class="rounded-art underline-offset-4 hover:text-chalk hover:underline focus-visible:outline-2 focus-visible:outline-primary-200 {className}"
	>{artist}</a
>
