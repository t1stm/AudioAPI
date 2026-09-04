import { beforeEach, expect, it, vi } from 'vitest';
import rooms from './rooms.svelte';
import { createRoom } from '$requests/rooms';

vi.mock('$requests/rooms', () => ({ createRoom: vi.fn() }));

const room = (roomID: string, description: string) => ({ roomID, name: roomID, description });

beforeEach(() => {
	// `mockReset: true` in the vitest config drops implementations between tests
	vi.mocked(createRoom).mockResolvedValue({ roomID: 'new', name: 'new', description: '' });
	// the server's command split leaves the leading space on stored descriptions
	rooms.list = [room('existing', ' discord:100'), room('other', ' discord:1000')];
});

it('rejoins the channel room instead of creating another', async () => {
	expect(await rooms.findOrCreateForDiscord('discord:100', 'x')).toBe('existing');
	expect(createRoom).not.toHaveBeenCalled();
});

it('does not mistake a longer snowflake for a prefix match', async () => {
	expect(await rooms.findOrCreateForDiscord('discord:10', 'x')).toBe('new');
	expect(createRoom).toHaveBeenCalledOnce();
});
