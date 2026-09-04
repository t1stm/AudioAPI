<script lang="ts">
	// A shape holding a slot's space until the track that belongs in it lands.
	let { class: klass = '' }: { class?: string } = $props();
</script>

<div class="animate-pulse rounded-art bg-surface-200 {klass}" aria-hidden="true"></div>
