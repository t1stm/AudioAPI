<script lang="ts">
	import current from '$states/current.svelte';
	import audio from '$states/audio.svelte';
	import queue from '$states/queue.svelte';
	import session from '$states/session.svelte';
	let url = $derived(current.url);
	let element: HTMLAudioElement | undefined = $state();

	// ponytail: not `bind:paused`. Its write runs in the same flush as a `src`
	// change, the load algorithm aborts the play(), and its catch writes
	// `paused = true` back — so the room says playing and this client sits
	// silent. `audio.paused` is the intent here; `oncanplay` re-applies it once
	// the element can honour it. Only a real autoplay block flips the intent.
	function apply() {
		if (!element) return;
		if (audio.paused) element.pause();
		else
			element.play().catch((error: DOMException) => {
				if (error.name === 'NotAllowedError') audio.paused = true;
			});
	}

	// a src change needs no dependency here: the element pauses itself on load and
	// `oncanplay` re-applies the intent to the new resource.
	$effect(apply);
</script>

<audio
	bind:this={element}
	src={url}
	preload="auto"
	bind:volume={audio.volume}
	bind:currentTime={audio.currentSeconds}
	oncanplay={apply}
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
		// in a room the server owns the advance: report once and wait for the
		// finishing barrier to release for everybody
		if (session.inRoom) session.reportEnded();
		else queue.nextTrack();
	}}
>
</audio>
