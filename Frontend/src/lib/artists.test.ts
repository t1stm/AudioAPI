import { describe, expect, it } from 'vitest';
import { heroArtist, splitArtists } from './artists';

describe('splitArtists', () => {
	it('gives every performer of a merged credit its own name', () => {
		expect(splitArtists('Stiliyan, Jamaikata, Alex Toploto')).toEqual([
			'Stiliyan',
			'Jamaikata',
			'Alex Toploto'
		]);
	});

	it('splits on an ampersand between names', () => {
		expect(splitArtists('DJ Chocolate & DJ Vickie')).toEqual(['DJ Chocolate', 'DJ Vickie']);
		expect(splitArtists('Слави Трифонов & Ку-ку Бенд, Ева квартет')).toEqual([
			'Слави Трифонов',
			'Ку-ку Бенд',
			'Ева квартет'
		]);
	});

	it('keeps an ampersand that is part of a name', () => {
		expect(splitArtists('Rad&Co')).toEqual(['Rad&Co']);
	});

	it('leaves a single artist whole', () => {
		expect(splitArtists('Despina Vandi')).toEqual(['Despina Vandi']);
	});

	it('has nothing to link when there is no artist', () => {
		expect(splitArtists(undefined)).toEqual([]);
		expect(splitArtists(' , ')).toEqual([]);
	});
});

describe('heroArtist', () => {
	it('asks for the lead artist, not the whole line-up', () => {
		expect(heroArtist('Stiliyan, Jamaikata, Alex Toploto')).toBe('Stiliyan');
	});

	it('passes a single artist through, and has nothing to ask without one', () => {
		expect(heroArtist('Despina Vandi')).toBe('Despina Vandi');
		expect(heroArtist(null)).toBe('');
	});
});
