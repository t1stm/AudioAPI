import { describe, expect, it } from 'vitest';
import { streamJson } from './streamJson';

const encoder = new TextEncoder();

/** A body that delivers exactly these chunks, so a split can be put anywhere. */
function bodyOf(chunks: string[]) {
	return new ReadableStream<Uint8Array>({
		start(controller) {
			for (const chunk of chunks) controller.enqueue(encoder.encode(chunk));
			controller.close();
		}
	});
}

async function collect<T>(chunks: string[]) {
	const out: T[] = [];
	for await (const value of streamJson<T>(bodyOf(chunks))) out.push(value);
	return out;
}

describe('streamJson', () => {
	it('reads a JSON array split at every awkward point', async () => {
		// mid-object, mid-string, immediately after a backslash, and the array's own
		// punctuation alone in a chunk
		expect(
			await collect([
				'[',
				'{"id":"a","name":"Br',
				'aces } and a \\',
				'" quote"}',
				',',
				'{"id":"b","name":"b"}',
				']'
			])
		).toEqual([
			{ id: 'a', name: 'Braces } and a " quote' },
			{ id: 'b', name: 'b' }
		]);
	});

	it('reads newline-delimited objects the same way', async () => {
		expect(await collect(['{"id":"a"}\n{"id', '":"b"}\n'])).toEqual([{ id: 'a' }, { id: 'b' }]);
	});

	it('hands over each object as it arrives, not at the end of the body', async () => {
		let push!: (text: string) => void;
		let close!: () => void;
		const body = new ReadableStream<Uint8Array>({
			start(controller) {
				push = (text) => controller.enqueue(encoder.encode(text));
				close = () => controller.close();
			}
		});

		const stream = streamJson<{ id: string }>(body);
		push('[{"id":"a"},');
		expect((await stream.next()).value).toEqual({ id: 'a' });

		push('{"id":"b"}]');
		close();
		expect((await stream.next()).value).toEqual({ id: 'b' });
		expect((await stream.next()).done).toBe(true);
	});

	it('keeps what completed when the body is cut short', async () => {
		// what a mid-enumeration failure looks like: the array can no longer become
		// an error status, it just stops
		expect(await collect(['[{"id":"a"},{"id":"b"'])).toEqual([{ id: 'a' }]);
	});
});
