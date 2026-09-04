export function getTimeString(seconds: number) {
	const totalSeconds = Math.max(0, Math.floor(Number.isFinite(seconds) ? seconds : 0));
	const hours = Math.floor(totalSeconds / 3600);
	const minutes = Math.floor((totalSeconds % 3600) / 60);
	const remainingSeconds = totalSeconds % 60;
	const pad = (value: number) => String(value).padStart(2, '0');

	return hours > 0 ? `${hours}:${pad(minutes)}:${pad(remainingSeconds)}` : `${minutes}:${pad(remainingSeconds)}`;
}

export function convertTimeSpanStringToSeconds(dateString: string): number {
	return dateString
		.split(':')
		.reverse()
		.reduce((previous, current, i) => previous + Number.parseInt(current) * Math.pow(60, i), 0);
}
