<script lang="ts">
	import current from '$states/current.svelte';
	import audio from '$states/audio.svelte';
	import queue from '$states/queue.svelte';
	let url = $derived(current.url);
</script>

<audio
	src={url}
	bind:paused={audio.paused}
	bind:volume={audio.volume}
	bind:currentTime={audio.currentSeconds}
	autoplay
	onloadstart={() => {
		// bufferedSeconds only ever climbs, so it has to go back to zero with the
		// resource it describes — otherwise the next track inherits this one's
		// buffer and the gauge never fills again.
		audio.bufferedSeconds = 0;
	}}
	onprogress={(event) => {
		const player = event.currentTarget;
		const buffer = player.buffered;
		if (buffer.length < 1) return;

		const end = buffer.end(buffer.length - 1);
		if (end < audio.bufferedSeconds) return;
		audio.bufferedSeconds = end;
	}}
	oncanplaythrough={() => {
		audio.bufferedSeconds = current.lengthSeconds;
	}}
	onended={() => {
		queue.nextTrack();
	}}
>
</audio>
