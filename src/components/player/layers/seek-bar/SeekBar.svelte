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

	let buffered = $derived((audio.bufferedSeconds / current.lengthSeconds) * 100);
	let currentPercentage = $derived((audio.currentSeconds / current.lengthSeconds) * 100);

	let currentTime = $derived(getTimeString(audio.currentSeconds));
	let maxTime = $derived(getTimeString(current.lengthSeconds));
</script>

<div id="seekbar" class="flex items-center gap-2 w-full max-w-lg">
	<span class="text-xs font-bold text-white select-none">{currentTime}</span>
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
			class="absolute left-0 h-full max-w-full bg-primary-500 rounded-lg duration-75"
			style:width={currentPercentage + '%'}
		></div>
		<div
			class="absolute left-0 -translate-x-1/2 rounded-full size-3 bg-white duration-75 transition-opacity"
			style:left={slider.hoverValue + '%'}
			style:opacity={slider.blipVisible ? 1 : 0}
		></div>
	</div>
	<span class="text-xs font-bold text-white select-none">{maxTime}</span>
</div>
