<script lang="ts">
	import current from '$states/current.svelte';
	import audio from '$states/audio.svelte';
	import queue from '$states/queue.svelte';
	import session from '$states/session.svelte';
	import { interpolate } from '$lib/playbackClock';
	let url = $derived(current.url);
	let element: HTMLAudioElement | undefined = $state();

	let context: AudioContext | undefined;
	// The element's clock is the inaccurate part: `timeupdate` reports it about
	// four times a second and some browsers quantise it on top of that. The
	// context's clock is the one the hardware actually plays against, so the
	// position is carried on that between the element's own reports, and every
	// report re-anchors the pair so the two can never drift apart.
	let anchorMedia = 0;
	let anchorClock = 0;
	// interpolating is only honest while the element is really moving; a stall
	// stops `timeupdate` but not the context clock, and the position would run
	// away from the sound.
	let advancing = false;

	// ponytail: `createMediaElementSource` may be called once per element for the
	// life of the page, and the element outlives every track — so the graph is
	// built once and never rebuilt. No GainNode: `element.volume` still applies
	// upstream of the node. Add one if a browser turns out to ignore it.
	$effect(() => {
		if (!element || context) return;
		const built = new AudioContext({ latencyHint: 'interactive' });
		built.createMediaElementSource(element).connect(built.destination);
		context = built;
		return () => {
			context = undefined;
			void built.close();
		};
	});

	function anchor() {
		if (!element) return;
		anchorMedia = element.currentTime;
		anchorClock = context ? context.currentTime : 0;
		// writing here too is what keeps a background tab moving: rAF is throttled
		// to a stop there, `timeupdate` is not.
		audio.currentSeconds = anchorMedia;
	}

	function hold() {
		advancing = false;
		anchor();
	}

	function resume() {
		advancing = true;
		anchor();
	}

	function tick() {
		if (!context || !advancing) return;
		// what you hear trails the clock by whatever the graph has not played out
		// yet; reporting the audible position is the point of doing this at all,
		// since a room syncs against sound, not buffers.
		audio.currentSeconds = interpolate(
			anchorMedia,
			context.currentTime - anchorClock,
			element?.playbackRate ?? 1,
			context.outputLatency || 0
		);
	}

	$effect(() => {
		if (!element || audio.paused) return;
		let frame = requestAnimationFrame(function next() {
			tick();
			frame = requestAnimationFrame(next);
		});
		return () => cancelAnimationFrame(frame);
	});

	// The room's clock steers the rate to hold everyone together. `preservesPitch`
	// off is deliberate: on, the browser time-stretches, and a phase vocoder is
	// audibly grainy on transients. Off, it resamples — and the loop settles
	// within a few parts in ten thousand of 1.0, which is a pitch shift of about
	// a cent. The clamp that keeps it there lives in `syncClock.ts`.
	$effect(() => {
		if (!element) return;
		element.preservesPitch = false;
		element.playbackRate = audio.rate;
	});

	// a write to the state the element did not make is a seek. The tolerance is
	// what stops this component's own writes from bouncing back in as one.
	$effect(() => {
		const seconds = audio.currentSeconds;
		if (element && Math.abs(element.currentTime - seconds) > 0.25) element.currentTime = seconds;
	});

	// ponytail: not `bind:paused`. Its write runs in the same flush as a `src`
	// change, the load algorithm aborts the play(), and its catch writes
	// `paused = true` back — so the room says playing and this client sits
	// silent. `audio.paused` is the intent here; `oncanplay` re-applies it once
	// the element can honour it. Only a real autoplay block flips the intent.
	function apply() {
		if (!element) return;
		if (audio.paused) return element.pause();

		// routed through the graph, a suspended context is silence no matter what
		// the element does — and it starts suspended until a gesture resumes it.
		void context?.resume();
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
	crossorigin="anonymous"
	bind:volume={audio.volume}
	onplaying={resume}
	ontimeupdate={anchor}
	onseeked={anchor}
	onpause={hold}
	onwaiting={hold}
	onstalled={hold}
	onemptied={hold}
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
		session.reportLoaded();
	}}
	onerror={() => {
		// a track this client can never play must not hold the barrier shut for
		// everybody else; answer it and sit the track out.
		session.reportLoaded();
	}}
	onended={() => {
		hold();
		// in a room the server owns the advance: report once and wait for the
		// finishing barrier to release for everybody
		if (session.inRoom) session.reportEnded();
		else queue.nextTrack();
	}}
>
</audio>
