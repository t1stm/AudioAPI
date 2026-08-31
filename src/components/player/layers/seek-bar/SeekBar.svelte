<script lang="ts">
	import audio from '$states/audio.svelte';
	import current from '$states/current.svelte';

	import { getTimeString } from '$lib';
	import { SliderInteractions } from '$lib/sliderInteractions.svelte.js';

	const seekSeconds = 2;
	const slider = new SliderInteractions(seekSeconds);

	slider.onChange = () => {
		audio.currentSeconds = (slider.percentage / 100) * current.lengthSeconds;
	};

	let buffered = $derived(current.lengthSeconds > 0 ? (audio.bufferedSeconds / current.lengthSeconds) * 100 : 0);
	let currentPercentage = $derived(current.lengthSeconds > 0 ? (audio.currentSeconds / current.lengthSeconds) * 100 : 0);
	// Playback needs ~3s of runway; the gauge fills toward that, so a full column
	// is the moment the track resumes rather than an arbitrary level.
	const runwaySeconds = 3;
	let bufferedAhead = $derived(Math.max(audio.bufferedSeconds - audio.currentSeconds, 0));
	let isBuffering = $derived(
		!audio.paused && current.lengthSeconds > 0 && bufferedAhead < runwaySeconds
	);
	let gaugeLevel = $derived(Math.min(bufferedAhead / runwaySeconds, 1) * 100);

	let currentTime = $derived(getTimeString(audio.currentSeconds));
	let maxTime = $derived(getTimeString(current.lengthSeconds));

  $effect(() => {
    // in a separate effect to avoid re-running on every audio time update
    navigator.mediaSession.setActionHandler('seekbackward', () => {
      slider.keydown({
        key: 'ArrowLeft'
      } as KeyboardEvent)
    })

    navigator.mediaSession.setActionHandler('seekforward', () => {
      slider.keydown({
        key: 'ArrowRight'
      } as KeyboardEvent)
    })
  })

  $effect(() => {
    const position = audio.currentSeconds;
    const length = current.lengthSeconds > audio.currentSeconds ? current.lengthSeconds : audio.currentSeconds;

    navigator.mediaSession.setPositionState({
      duration: length,
      position: position,
      playbackRate: 1,
    })
  })
</script>

<div id="seekbar" class="flex items-center gap-2 w-full max-w-lg">
	<div
		class="relative h-7 w-3.5 shrink-0 overflow-hidden rounded-art border bg-dark-0 transition-opacity duration-150"
		class:opacity-0={!isBuffering}
		class:border-primary-0={isBuffering}
		class:border-haze={!isBuffering}
		role="img"
		aria-label="Buffering"
		aria-hidden={!isBuffering}
	>
		<div class="rain-streak"></div>
		<div
			class="absolute inset-x-0 bottom-0 border-t border-primary-500 bg-primary-0/40 duration-150"
			style:height={gaugeLevel + '%'}
		></div>
	</div>
	<span class="font-mono text-xs text-fog select-none">{currentTime}</span>
	<div
		class="flex h-2 hover:h-3 rounded-lg w-full relative bg-surface-200 cursor-pointer duration-150 focus-visible:h-3 focus-visible:outline-4 outline-surface-300"
		tabindex="0"
		role="slider"
		aria-valuenow={currentPercentage}
		aria-valuemin="0"
		aria-valuemax="100"
		onfocusin={slider.enter}
		onmouseenter={slider.enter}
		onmouseleave={slider.leave}
		onfocusout={slider.leave}
		onmouseup={slider.mouseUp}
		onmousedown={slider.mouseDown}
		onmousemove={slider.mouseMove}
		onkeydown={slider.keydown}
	>
		<div
			class="absolute left-0 h-full max-w-full bg-primary-0 rounded-lg duration-75"
			style:width={buffered + '%'}
		></div>
		<div
			class="absolute left-0 h-full max-w-full rounded-lg duration-150"
			class:bg-primary-500={!isBuffering}
			class:bg-surface-400={isBuffering}
			style:width={currentPercentage + '%'}
		></div>
		<div
			class="absolute left-0 -translate-x-1/2 rounded-full size-3 bg-white duration-75 transition-opacity"
			style:left={slider.hoverValue + '%'}
			style:opacity={slider.blipVisible ? 1 : 0}
		></div>
	</div>
	<span class="font-mono text-xs text-fog select-none">{maxTime}</span>
</div>
