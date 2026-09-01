import { describe, expect, it } from 'vitest';
import { interpolate } from './playbackClock';

describe('interpolate', () => {
	it('carries the anchor forward on the context clock', () => {
		expect(interpolate(10, 0.25, 1, 0)).toBeCloseTo(10.25);
	});

	it('scales elapsed context time by the playback rate, not the anchor', () => {
		expect(interpolate(10, 2, 1.5, 0)).toBeCloseTo(13);
	});

	it('reports the audible position, behind the clock by the output latency', () => {
		expect(interpolate(10, 0.25, 1, 0.05)).toBeCloseTo(10.2);
	});

	it('never reports a negative position when latency outruns the start', () => {
		expect(interpolate(0, 0.01, 1, 0.05)).toBe(0);
	});
});
