import { describe, expect, it } from 'vitest';
import { drop, dropAbove, push, top } from './backStack';

/** Every test starts from an empty stack; the module keeps one for the whole page. */
function reset() {
	dropAbove(0);
}

describe('backStack', () => {
	it('closes the innermost layer first', () => {
		reset();
		const closed: string[] = [];
		push(1, () => closed.push('dock'));
		push(2, () => closed.push('folder'));
		push(3, () => closed.push('popover'));

		dropAbove(2);
		expect(closed).toEqual(['popover']);
		dropAbove(1);
		expect(closed).toEqual(['popover', 'folder']);
		dropAbove(0);
		expect(closed).toEqual(['popover', 'folder', 'dock']);
	});

	it('skips a layer that closed itself, and still closes the one above it', () => {
		reset();
		const closed: string[] = [];
		const dock = push(1, () => closed.push('dock'));
		push(2, () => closed.push('folder'));

		// the dock's own button, pressed while a folder is open below it
		expect(drop(dock)).toBe(true);
		expect(drop(dock)).toBe(false);

		// one back press, and it reaches the folder rather than being spent on the dock
		dropAbove(1);
		expect(closed).toEqual(['folder']);
	});

	it('drops nothing when a layer was already taken by a close request', () => {
		reset();
		const layer = push(1, () => {});
		dropAbove(0);
		expect(drop(layer)).toBe(false);
	});

	it('reports the innermost layer', () => {
		reset();
		expect(top()).toBeNull();
		push(1, () => {});
		const inner = push(2, () => {});
		expect(top()).toBe(inner);
		drop(inner);
		expect(top()?.depth).toBe(1);
	});
});
