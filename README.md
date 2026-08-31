# A (Future) Game-Changing Music Listening Platform
Written in SvelteKit + Vite

## Currently Supports
* All features without joining a room.
* Creating rooms and joining them.
* Controlling the rooms with syncing between other users.
* ~50ms max total latency between all users (without including the network).

## Future Features

* Playlist support.
* Migrating playlists from other platforms.
* Multiple server selection with community-driven servers.
* React-Native port.
* Offline support.

## Status
Currently running at https://music.gergov.bg/

## Running as a Discord Activity

1. Set `VITE_DISCORD_CLIENT_ID` in a `.env` file (your application's ID).
2. In the Developer Portal, enable Activities and add these **URL Mappings**:

   | Prefix | Target |
   | --- | --- |
   | `/` | the frontend host (`music.gergov.bg`, or your dev tunnel host) |
   | `/api` | `api.gergov.bg` |
   | `/ytimg/{subdomain}` | `{subdomain}.ytimg.com` |

   The activity iframe blocks every host that isn't mapped, so the client rewrites
   API and thumbnail URLs to `/.proxy/<prefix>` when it detects it's embedded
   (`src/lib/discord.ts`). Unmapped thumbnail hosts fall back to `/empty.png`.

3. For local development, serve `npm run dev` over HTTPS — e.g.
   `cloudflared tunnel --url http://localhost:5173` — and point the `/` mapping at
   the tunnel host. Then launch the activity from a voice channel.

Outside Discord nothing changes: `isDiscordActivity` is false, URLs stay absolute
and the SDK is never constructed.

**Next step (multiplayer):** after `initDiscord()` resolves, `sdk.channelId`,
`sdk.guildId` and `sdk.instanceId` identify the room the activity launched in.
