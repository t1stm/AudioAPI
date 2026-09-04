import { describe, expect, it, vi } from 'vitest';
import { SliderInteractions } from './sliderInteractions.svelte.js';

// A 200px-wide track starting at x=100, plus the capture call pointerDown makes.
function press(clientX: number) {
	const captured: number[] = [];
	return {
		captured,
		event: {
			pointerId: 1,
			clientX,
			currentTarget: {
				getBoundingClientRect: () => ({ left: 100, width: 200 }),
				setPointerCapture: (id: number) => captured.push(id)
			}
		} as unknown as PointerEvent & { currentTarget: EventTarget & HTMLDivElement }
	};
}

describe('slider pointer interactions', () => {
	it('positions from the press itself, because touch has no hover first', () => {
		const slider = new SliderInteractions(2);
		const onChange = vi.fn();
		slider.onChange = onChange;

		const { event, captured } = press(150);
		slider.pointerDown(event);

		expect(slider.percentage).toBe(25);
		expect(captured).toEqual([1]);
		expect(onChange).toHaveBeenCalledOnce();
	});

	it('ignores a move that follows no press', () => {
		const slider = new SliderInteractions(2, 40);
		const onChange = vi.fn();
		slider.onChange = onChange;

		slider.pointerMove(press(150).event);

		expect(slider.hoverValue).toBe(25);
		expect(slider.percentage).toBe(40);
		expect(onChange).not.toHaveBeenCalled();
	});

	it('tracks a drag until pointerup, and clamps past either end', () => {
		const slider = new SliderInteractions(2);
		slider.pointerDown(press(150).event);

		slider.pointerMove(press(400).event);
		expect(slider.percentage).toBe(100);
		slider.pointerMove(press(0).event);
		expect(slider.percentage).toBe(0);

		// leaving the track mid-drag must not end it — capture keeps it alive
		slider.leave();
		slider.pointerMove(press(200).event);
		expect(slider.percentage).toBe(50);

		slider.pointerUp();
		slider.pointerMove(press(250).event);
		expect(slider.percentage).toBe(50);
	});
});
