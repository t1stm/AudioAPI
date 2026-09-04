import { expect, it } from 'vitest';
import user from './user.svelte';

// `load()` restores a stored name before `initDiscord()` resolves, so adopting
// has to win over an already-chosen one — otherwise the activity still stops on
// the room page's name gate.
it('adopts the Discord identity over an existing local name', () => {
	user.choose('local name');
	user.adopt('discord_name', 'https://cdn.discordapp.com/avatars/1/a.png?size=64');

	expect(user.chosen).toBe(true); // the name gate is skipped
	expect(user.username).toBe('discord_name');
	expect(user.avatarUrl).toContain('cdn.discordapp.com');
	expect(user.source).toBe('discord');
});
