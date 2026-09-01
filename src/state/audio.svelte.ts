class Audio {
	volume: number = $state(0.2);
	currentSeconds: number = $state(0);
	bufferedSeconds: number = $state(0);
	paused: boolean = $state(false);
	/** What the player is asked to run at. The room's clock steers this within
	 *  a couple of percent to hold everyone together; nothing else writes it. */
	rate: number = $state(1);

	/** The exact audible position right now, in seconds.
	 *
	 *  `currentSeconds` is a 10 Hz sample of this for the UI, and the room's clock
	 *  must not use that: `SyncClock.sample` subtracts the local position from the
	 *  server's, and picks the winning sample by round trip rather than by
	 *  freshness — so however long ago the display last ticked lands in the sync
	 *  error unfiltered, against a 25 ms deadband. `Audio.svelte` installs the real
	 *  one; this fallback is what runs before the player mounts. */
	positionNow: () => number = () => this.currentSeconds;
}

export default new Audio();
