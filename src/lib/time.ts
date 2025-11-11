export function getTimeString(seconds: number) {
	const date = new Date(0);
	date.setSeconds(seconds);
	return date.toISOString().substring(11, 19);
}

export function convertTimeSpanStringToSeconds(dateString: string): number {
	return dateString
		.split(':')
		.reverse()
		.reduce((previous, current, i) => previous + Number.parseInt(current) * Math.pow(60, i), 0);
}
