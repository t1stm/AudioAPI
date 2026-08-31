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
	])

  $effect(() => {
    // static navigator fields are not very reactive. shouldn't update them every time
    navigator.mediaSession.setActionHandler('play', () => {
      audio.paused = false;
    });
    navigator.mediaSession.setActionHandler('pause', () => {
      audio.paused = true;
    });
    navigator.mediaSession.setActionHandler('previoustrack', () => {
      queue.previousTrack();
    });
    navigator.mediaSession.setActionHandler('nexttrack', () => {
      queue.nextTrack();
    });
  })

  $effect(() => {
    navigator.mediaSession.playbackState = audio.paused ? 'paused' : 'playing'
  })
</script>

<div id="controls" class="flex shrink-0 gap-2">
	{#each buttons as button (button.icon)}
		<button
			class="cursor-pointer ring-0 focus-visible:bg-surface-300 focus-visible:outline-5 outline-surface-300 rounded-lg duration-75"
			onclick={button.onClick}
		>
			<Icon src={button.icon} color="white" mini size="24" />
		</button>
	{/each}
</div>
