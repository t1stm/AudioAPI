<script lang="ts">
	import TrackInfo from './layers/track-info/TrackInfo.svelte';
	import Controls from './layers/controls/Controls.svelte';
	import Volume from './layers/volume/Volume.svelte';
	import Quality from './layers/quality/Quality.svelte';
	import SeekBar from './layers/seek-bar/SeekBar.svelte';
	import { Icon, QueueList } from 'svelte-hero-icons';
	import Audio from '$components/player/layers/audio/Audio.svelte';
	import queue from '$states/queue.svelte';

	let { showQueue = $bindable(false) }: { showQueue?: boolean } = $props();
</script>

<div
	id="player"
	class="absolute bottom-2 left-2 right-2 z-10 flex w-auto flex-col items-center justify-between gap-2 rounded-panel border border-haze bg-surface-100/85 px-3 py-2 backdrop-blur-xl sm:min-h-[53px] sm:bottom-4 sm:left-1/2 sm:right-auto sm:w-[min(100%-2rem,80rem)] sm:-translate-x-1/2 sm:flex-row sm:gap-0 sm:py-1 sm:px-4"
>
	<Controls />
	<div class="flex w-full min-w-0 items-center justify-center gap-3 sm:gap-4">
		<TrackInfo />
		<SeekBar />
	</div>
	<div class="flex shrink-0 items-center gap-2">
		<label class="relative cursor-pointer rounded-art p-1 text-fog hover:text-chalk" class:bg-surface-200={showQueue}
			class:text-chalk={showQueue}>
			<input type="checkbox" bind:checked={showQueue} class="hidden" />
			<Icon src={QueueList} mini size="16" />
			<span class="absolute -right-1 -top-1 min-w-4 rounded-full bg-primary-600 px-1 text-center font-mono text-[9px] font-medium leading-4 text-white">{queue.items.length}</span>
		</label>
		<Quality />
		<Volume />
		<Audio />
	</div>

</div>
