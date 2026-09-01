<script lang="ts">
	import audio from '$states/audio.svelte';
	import { Icon, SpeakerWave, SpeakerXMark } from 'svelte-hero-icons';
	import { SliderInteractions } from '$lib/sliderInteractions.svelte.js';

	const slider = new SliderInteractions(5, 50);
	let icon = $derived((slider.percentage > 0) ? SpeakerWave : SpeakerXMark);

	$effect(() => {
		audio.volume = slider.percentage / 100
	});
</script>

<div class="flex relative w-24 items-center gap-2">
	<Icon src={icon} mini size="20" color="white" class="cursor-pointer" />
	<div
		class="group relative flex h-1 w-full cursor-pointer touch-none rounded-full bg-surface-200 ring-0 duration-150 hover:h-2 focus-visible:h-2 focus-visible:outline-4 outline-surface-300"
		onpointerenter={slider.enter}
		onpointerleave={slider.leave}
		onpointerdown={slider.pointerDown}
		onpointermove={slider.pointerMove}
		onpointerup={slider.pointerUp}
		onpointercancel={slider.pointerUp}
		onfocusin={slider.enter}
		onfocusout={slider.leave}
		onkeydown={slider.keydown}
		onwheel={(event) => {
			const step = -event.deltaY / 100;
			slider.hoverValue = slider.percentage = Math.max(
				Math.min((slider.percentage += step), 100),
				0
			);
		}}
		role="button"
		tabindex="0"
	>
		<div
			class="flex h-full rounded-full bg-primary-0 duration-150"
			style:width={slider.percentage + '%'}
		></div>
		<div
			class="bg-white left-0 absolute size-1 group-hover:size-2 group-focus-visible:size-2 rounded-full duration-75 -translate-x-1/2 opacity-0"
			style:opacity={slider.blipVisible ? 1 : 0}
			style:left={slider.hoverValue + '%'}
		></div>
	</div>
</div>
