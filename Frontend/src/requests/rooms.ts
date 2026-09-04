import { audioApi } from '$lib/discord';
import { AudioApiError } from './songs';

export type Room = {
	roomID: string;
	name: string;
	description: string;
};

/** A fresh room's name is its own GUID and its description is empty; rename over
 *  the session socket (`updateroom`), not over HTTP. */
export async function createRoom(): Promise<Room> {
	const response = await fetch(`${audioApi}/Multiplayer/CreateRoom`, {
		method: 'POST',
	});
	if (!response.ok)
		throw new AudioApiError(
			`Could not start a room — the audio service returned ${response.status}.`,
			response.status,
		);

	return (await response.json()) as Room;
}

/** Only the first space splits a command server-side, so free-text arguments keep
 *  the separator and come back with a leading space. Trim everything on receipt. */
export function roomLabel(room: Room) {
	return room.name.trim();
}

/** A room nobody ever renamed still carries its own GUID as its name — the only
 *  occupancy-shaped fact the payload has. */
export function isUnnamed(room: Room) {
	return roomLabel(room) === room.roomID;
}
