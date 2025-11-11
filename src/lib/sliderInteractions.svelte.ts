export class SliderInteractions {
	isMouseDown = $state(false);
	blipVisible = $state(false);

	stepValue = 2;
	percentage = $state(0);
	hoverValue = $state(0);
	onChange: () => void = () => {};
	constructor(stepValue: number, percentage = 0) {
		this.stepValue = stepValue;
		this.percentage = percentage;
	}

	mouseMove = (event: MouseEvent & { currentTarget: EventTarget & HTMLDivElement }) => {
		const rect = event.currentTarget?.getBoundingClientRect();
		if (!rect) return;
		this.hoverValue = Math.max(Math.min((event.clientX - rect.left) / rect.width, 1), 0) * 100;
		if (!this.isMouseDown) return;

		this.percentage = this.hoverValue;
		this.onChange();
	};

	mouseDown = () => {
		this.percentage = this.hoverValue;
		this.isMouseDown = true;
		this.onChange();
	};

	mouseUp = () => {
		this.isMouseDown = false;
	};

	enter = () => {
		this.blipVisible = true;
	};

	leave = () => {
		this.blipVisible = false;
		this.isMouseDown = false;
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
