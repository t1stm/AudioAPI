class Audio {
	volume: number = $state(0.2);
	currentSeconds: number = $state(0);
	bufferedSeconds: number = $state(0);
	paused: boolean = $state(false);
}

export default new Audio();
