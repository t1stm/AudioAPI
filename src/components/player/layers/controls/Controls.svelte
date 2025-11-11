<script lang="ts">
	import { Backward, Forward, Icon, Pause, Play } from 'svelte-hero-icons';
	import audio from '$states/audio.svelte';
	import queue from '$states/queue.svelte';

	const buttons = $derived([
		{
			icon: Backward,
			onClick: () => queue.previousTrack()
		},
		{
			icon: !audio.paused ? Pause : Play,
			onClick: () => {
				audio.paused = !audio.paused;
			}
		},
		{
			icon: Forward,
			onClick: () => queue.nextTrack()
		}
	]);
</script>

<div id="controls" class="flex gap-2">
	{#each buttons as button (button.icon)}
		<button
			class="cursor-pointer ring-0 focus-visible:bg-surface-300 focus-visible:outline-5 outline-surface-300 rounded-lg duration-75"
			onclick={button.onClick}
		>
			<Icon src={button.icon} color="white" mini size="24" />
		</button>
	{/each}
</div>
