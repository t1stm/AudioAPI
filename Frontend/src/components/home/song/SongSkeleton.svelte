<script lang="ts">
	import Skeleton from '$components/Skeleton.svelte';
</script>

<!-- The Song card's own box, to the pixel: a filled slot and an empty one are the
     same size, so a row never shifts as it fills. -->
<div class="flex w-36 shrink-0 flex-col gap-2 sm:w-48">
	<Skeleton class="size-36 sm:size-48" />
	<div class="grid gap-1.5 py-1">
		<Skeleton class="h-3 w-4/5" />
		<Skeleton class="h-3 w-3/5" />
	</div>
</div>
