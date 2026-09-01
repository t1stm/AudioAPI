class Audio {
	volume: number = $state(0.2);
	currentSeconds: number = $state(0);
	bufferedSeconds: number = $state(0);
	paused: boolean = $state(false);
	/** What the player is asked to run at. The room's clock steers this within
	 *  a couple of percent to hold everyone together; nothing else writes it. */
	rate: number = $state(1);
}

export default new Audio();
