import { describe, expect, it } from 'vitest';
import { at, record, type Roll } from './rollHistory';

const roll = (name: string): Roll => ({
	hero: { name } as Roll['hero'],
	artistSongs: [],
	picks: [],
	variant: null
});

describe('rollHistory', () => {
	it('hands back every roll it was given, by index', () => {
		const first = record(roll('first'));
		const second = record(roll('second'));

		expect(second).toBe(first + 1);
		expect(at(first)?.hero?.name).toBe('first');
		expect(at(second)?.hero?.name).toBe('second');
	});

	it('returns null for an index this page load never drew', () => {
		const index = record(roll('only'));
		expect(at(index + 1)).toBeNull();
		expect(at(-1)).toBeNull();
	});
});
