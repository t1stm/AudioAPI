<script lang="ts">
	import TrackInfo from './layers/track-info/TrackInfo.svelte';
	import Controls from './layers/controls/Controls.svelte';
	import Volume from './layers/volume/Volume.svelte';
	import Quality from './layers/quality/Quality.svelte';
	import SeekBar from './layers/seek-bar/SeekBar.svelte';
	import { ChatBubbleOvalLeft, Icon, QueueList } from 'svelte-hero-icons';
	import Audio from '$components/player/layers/audio/Audio.svelte';
	import queue from '$states/queue.svelte';
	import session from '$states/session.svelte';

	type Dock = 'queue' | 'chat' | null;
	let { dock = $bindable<Dock>(null) }: { dock?: Dock } = $props();

	function toggle(tab: Exclude<Dock, null>) {
		dock = dock === tab ? null : tab;
	}
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
		<!-- chat is reachable on every route, in a room or not: outside one its
		     empty state is the feature's front door -->
		<button
			type="button"
			aria-label="Open chat"
			aria-pressed={dock === 'chat'}
			class="relative cursor-pointer rounded-art p-1 text-fog hover:text-chalk focus-visible:outline-2 focus-visible:outline-primary-200"
			class:bg-surface-200={dock === 'chat'}
			class:text-chalk={dock === 'chat'}
			onclick={() => toggle('chat')}
		>
			<Icon src={ChatBubbleOvalLeft} mini size="16" />
			{#if session.unread > 0}
				<span class="absolute -right-1 -top-1 min-w-4 rounded-full bg-primary-600 px-1 text-center font-mono text-[9px] font-medium leading-4 text-white">{session.unread}</span>
			{/if}
		</button>
		<button
			type="button"
			aria-label="Open queue"
			aria-pressed={dock === 'queue'}
			class="relative cursor-pointer rounded-art p-1 text-fog hover:text-chalk focus-visible:outline-2 focus-visible:outline-primary-200"
			class:bg-surface-200={dock === 'queue'}
			class:text-chalk={dock === 'queue'}
			onclick={() => toggle('queue')}
		>
			<Icon src={QueueList} mini size="16" />
			<span class="absolute -right-1 -top-1 min-w-4 rounded-full bg-primary-600 px-1 text-center font-mono text-[9px] font-medium leading-4 text-white">{queue.items.length}</span>
		</button>
		<Quality />
		<Volume />
		<Audio />
	</div>

</div>
