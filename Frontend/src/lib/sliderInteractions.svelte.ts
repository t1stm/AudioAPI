type TrackPointerEvent = PointerEvent & { currentTarget: EventTarget & HTMLDivElement };

export class SliderInteractions {
	isPointerDown = $state(false);
	blipVisible = $state(false);

	stepValue = 2;
	percentage = $state(0);
	hoverValue = $state(0);
	onChange: () => void = () => {};
	constructor(stepValue: number, percentage = 0) {
		this.stepValue = stepValue;
		this.percentage = percentage;
	}

	pointerMove = (event: TrackPointerEvent) => {
		const rect = event.currentTarget?.getBoundingClientRect();
		if (!rect?.width) return;
		this.hoverValue = Math.max(Math.min((event.clientX - rect.left) / rect.width, 1), 0) * 100;
		if (!this.isPointerDown) return;

		this.percentage = this.hoverValue;
		this.onChange();
	};

	pointerDown = (event: TrackPointerEvent) => {
		// A finger has no hover, so the press is the only thing that says where it
		// landed — read the position off this event instead of the last move.
		// Capture keeps a drag alive once it wanders off an 8px-tall track, which
		// is most of them, and guarantees the matching pointerup lands here.
		event.currentTarget.setPointerCapture?.(event.pointerId);
		this.isPointerDown = true;
		this.pointerMove(event);
	};

	pointerUp = () => {
		this.isPointerDown = false;
	};

	enter = () => {
		this.blipVisible = true;
	};

	leave = () => {
		this.blipVisible = false;
	};

	keydown = (event: KeyboardEvent) => {
		const minVolume = 0;
		const maxVolume = 100;

		switch (event.key) {
			case 'ArrowDown':
			case 'ArrowLeft':
				this.hoverValue = this.percentage = Math.max(this.percentage - this.stepValue, minVolume);
				this.onChange();
				break;

			case 'ArrowUp':
			case 'ArrowRight':
				this.hoverValue = this.percentage = Math.min(this.percentage + this.stepValue, maxVolume);
				this.onChange();
				break;
		}
	};
}
