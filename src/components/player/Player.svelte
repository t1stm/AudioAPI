<script lang="ts">
	import TrackInfo from './layers/track-info/TrackInfo.svelte';
	import Controls from './layers/controls/Controls.svelte';
	import Volume from './layers/volume/Volume.svelte';
	import SeekBar from './layers/seek-bar/SeekBar.svelte';
	import { ChatBubbleOvalLeft, Icon, QueueList } from 'svelte-hero-icons';
	import Audio from '$components/player/layers/audio/Audio.svelte';
	import Queue from '$components/queue/Queue.svelte';

	let showQueue = $state(false);
</script>

<div
	id="player"
	class="absolute w-full max-w-7xl sm:h-14 bottom-0 left-1/2 -translate-x-1/2 mb-4 bg-surface-0/60 backdrop-blur-xl rounded-lg
flex flex-col sm:flex-row items-center py-1 px-4 box-border justify-between z-10"
>
	<Controls />
	<div class="flex w-full justify-center gap-4">
		<TrackInfo />
		<SeekBar />
	</div>
	<div class="flex gap-2 items-center">
		<Icon src={ChatBubbleOvalLeft} color="white" mini size="16" />
		<label class="cursor-pointer">
			<input type="checkbox" bind:checked={showQueue} class="hidden" />
			<Icon src={QueueList} color="white" mini size="16" />
		</label>
		<Volume />
		<Audio />
	</div>

	{#if showQueue}
		<Queue />
	{/if}
</div>
