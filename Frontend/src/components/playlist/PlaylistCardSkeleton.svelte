<script lang="ts">
	import Skeleton from '$components/Skeleton.svelte';
</script>

<!-- PlaylistCard's own box: the state rail, the square cover, the name and the count. -->
<div class="flex flex-col overflow-hidden rounded-panel border border-haze bg-surface-100">
	<span class="h-px w-full shrink-0 bg-haze"></span>
	<div class="p-2">
		<Skeleton class="aspect-square w-full" />
		<Skeleton class="mt-2 h-3.5 w-4/5" />
		<Skeleton class="mt-1.5 h-2.5 w-2/5" />
	</div>
</div>
