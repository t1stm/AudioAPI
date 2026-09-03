# Implement rooms, chat, and the shared clock

Companion to [`PLAN.md`](./PLAN.md). The visual direction, wireframes, screen
states, and copy live in the **Rooms and the Hold** design artifact; this file is
the build order, the file-by-file diff surface, and the protocol quirks to code
around. Source of truth for the API is `~/RiderProjects/AudioAPI/MULTIPLAYER_API.md`.

## What the API actually gives us

Three endpoints, no auth, no accounts:

|                                                          |                                                                         |
| -------------------------------------------------------- | ----------------------------------------------------------------------- |
| `POST /Audio/Multiplayer/CreateRoom`                     | no body, returns `{ roomID, name, description }`                        |
| `WS /Audio/Multiplayer/Rooms`                            | server push only, full room array on connect and on every create/rename |
| `WS /Audio/Multiplayer/Join?room={guid}&username={name}` | the session protocol                                                    |

Four absences shape every decision below, and none of them are bugs to design
around later:

- **No occupancy.** There is no member count, no user list, no presence frame.
  Rooms cannot honestly show "4 listening". The UI must never invent it.
- **No deletion.** Rooms live until the server restarts. The lobby is
  append-only and will fill with abandoned GUID-named rooms.
- **No errors.** Every malformed command is a silent no-op. Validate client-side;
  never wait for a reply that will not come.
- **No identity.** The user key is the connection's trace identifier. A reconnect
  is a new user: the old one leaves, a fresh join notice broadcasts, and
  `username` can only be set in the query string of a _new_ connection.

## Routes

`/rooms` — the lobby. `/room?id=<guid>` — one session. Query-param form matches
the existing `/search?term=` and `/artist?term=` convention.

## Step 1. Websocket base URL and room requests

`src/lib/discord.ts` already resolves `audioApi` to either
`https://api.gergov.bg/Audio` or `/.proxy/api/Audio`. Add the socket origin
beside it:

```ts
export const audioWs = new URL(audioApi, location.origin).href.replace(/^http/, 'ws');
```

`https://` becomes `wss://`, `http://localhost` becomes `ws://`, and the Discord
proxy path picks up `location.origin` first. Discord's activity proxy passes
websocket upgrades through the same URL mappings as HTTP, so no new mapping.

Also export `discordInstanceId` / `discordChannelId` from the already-initialised
`sdk` — Step 6 needs them and they are readable without authenticating.

New `src/requests/rooms.ts`: one `createRoom()` wrapping the POST, reusing the
`AudioApiError` shape from `src/requests/songs.ts`.

## Step 2. Identity — `src/state/user.svelte.ts`

The file is a dead stub today (`export const user = $state(null)`). Make it the
identity store: `username`, `avatarUrl`, and `source: 'local' | 'discord'`,
persisted under `musicrain.username` with the same guarded-`localStorage` shape
as `src/lib/recentlyPlayed.ts`.

The header's avatar button already exists with `aria-label="Open profile"` and no
handler — hang the name panel off it rather than adding a new control.

Because the server applies `username` only at connection registration, a name
change while connected must tear the Join socket down and reopen it. That
produces a leave notice and a rejoin notice in everyone's chat. Make the panel
say so before it saves, and leave the field disabled mid-track if that reads
worse in practice.

## Step 3. The lobby list — `src/state/rooms.svelte.ts`

A single class owning the `/Rooms` socket: `rooms`, `connected`, `connect()`,
`disconnect()`. Every frame replaces the whole array; there is no delta format,
so no merge logic. Never send on this socket.

Reconnect with backoff (1s doubling to 30s, reset on open) — the same helper
serves Step 4, so write it once here.

Trim on receipt. `updateroom name My Room` stores `" My Room"` because only the
first space splits a command, and the leading space arrives in the room list too.

`/rooms` filters client-side over `name` + `description` with a `$derived` —
the list is already in memory and there is no server-side search.

A room whose `name === roomID` has never been renamed. Segment on that: named
rooms first, unnamed below a divider. It is the only honest signal the payload
carries about whether anyone has ever used a room.

The room page keeps this socket open too. Renames confirm to the sender only
(§3.9), so the title in a session updates for everybody else solely through the
lobby feed.

## Step 4. The session — `src/state/session.svelte.ts`

One class, same shape as `queue`/`audio`/`current`: the Join socket, the room
name/description, the chat log, the roster, and the barrier bookkeeping.

Connect: `${audioWs}/Multiplayer/Join?room=${id}&username=${encodeURIComponent(name)}`.

**Room does not exist** is not an error frame — the socket is accepted, then
closed with `NormalClosure` having sent nothing. Count frames; a close at zero
frames means the room is gone.

Inbound dispatch, longest prefix first (`room name` and `room description` are
two-token prefixes, and `sync` is not `seek`):

| Frame                        | Do                                                                                                                            |
| ---------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `queue <json>`               | map to `SearchResult[]`, assign `queue.items`                                                                                 |
| `current <n>`                | `queue.currentIndex = n`, `current.set(items[n])` if it exists, `audio.paused = true`, then send `loaded` **once for this n** |
| `playing True\|False`        | `audio.paused = !…`                                                                                                           |
| `seek <t>`                   | `audio.currentSeconds = t`                                                                                                    |
| `sync <t>`                   | correct only if `abs(audio.currentSeconds - t) > 0.5`                                                                         |
| `stop`                       | `audio.paused = true`                                                                                                         |
| `chat <user> %% <text>`      | split on the **first** `%%`, trim both halves                                                                                 |
| `room name\|description <v>` | trimmed, into session state                                                                                                   |

Guard `queue[current]` — `next` can move `current` one past the end, which means
"nothing playing", not a bug.

Send `loaded` and `end` exactly once per track. Both barriers count _messages_,
not distinct users, so a double send releases the barrier early for the whole
room. A `loadedFor: number | null` compared against the incoming index is the
whole mechanism.

Drift correction: one `setInterval` sending `sync` every 10s while connected.

Chat has no history — you see only what arrives after you join. The roster is
built client-side from `chat System %% User 'x' joined/left …` notices and is
therefore incomplete on join and rebuilt from scratch on reconnect. Treat it as
"who has spoken or arrived since you did", and say so in the UI rather than
labelling it a member list.

Escaping: room names and usernames are unvalidated server-side. Svelte's `{}`
escapes; do not reach for `{@html}` anywhere in chat or room titles.

## Step 5. Route the queue verbs at the room

`PLAN.md` Step 4 built the queue dock against a verb table reserved for exactly
this. Honour it: when `session.connected`, each verb sends a command instead of
mutating locally, and the server's `queue`/`current` frames are the only writers.

| Local                                      | Connected                           |
| ------------------------------------------ | ----------------------------------- |
| `queue.add(item)`                          | `add <item.id>`                     |
| `queue.removeIndex(i)`                     | `remove <i>`                        |
| `queue.setNext(i)`                         | `setnext <i>`                       |
| `queue.playIndex(i)`                       | `skipto <i>`                        |
| `queue.nextTrack()` / `previousTrack()`    | `next` / `previous`                 |
| `queue.shuffle()`                          | `shuffle`                           |
| `audio.paused` toggle in `Controls.svelte` | `playpause`                         |
| seek in `SeekBar.svelte`                   | `seek <seconds>`                    |
| `queue.clear()`                            | hide it — the protocol has no clear |

Two shapes to reconcile:

- Room `queue` items are the raw platform result, not `SearchResult`. There is no
  `contentUrl`, and `name`/`artist` may be `null` (search results are normalised;
  queue items are not). Write one `roomItemToSearchResult()`: prefer
  `originalTitle`/`originalArtist` when present, fall back to `name`/`artist`,
  then to "Unknown title"/"Unknown artist".
- `SearchResult.contentUrl` becomes optional, and `SearchRow.svelte:130`'s
  "Download raw" link hides when it is absent. `current.set()` needs no change —
  it already builds the stream URL from `id`.

`playpause` is ignored server-side before the first track has started, and
`stop` does not stop the server's clock — the shared position keeps advancing.
Use `playpause` for pause; keep `stop` for leaving.

`src/components/player/layers/audio/Audio.svelte`: `onended` sends `end` when
connected instead of calling `queue.nextTrack()`, and `autoplay` becomes
`{!session.connected}` so a `current` frame loads without playing.

The last user leaving re-evaluates both barriers against a smaller member count,
which can fire the loaded barrier or skip a track. Rejoining an idle room may
land you further along than you left it. Nothing to fix — do not treat the jump
as a desync.

## Step 6. Discord

Available without authenticating, straight off the initialised `sdk`:
`channelId`, `guildId`, `instanceId`. Everyone who launches the activity from the
same voice channel shares `instanceId`, which is the natural room key.

Since `CreateRoom` takes no body, mark the room after creating it:

```
POST CreateRoom  →  updateroom description discord:<instanceId>
                 →  updateroom name <channel name or "Voice channel">
```

On launch, read the `/Rooms` feed and join the room whose `description` contains
`discord:<instanceId>`; create one when there is none. Two clients starting
together can both create — jitter the create by a random 0–400ms and re-check the
feed first. Worst case is a duplicate room, not a broken session.

Username inside Discord needs the real display name, and that needs
`sdk.commands.authenticate()`, which needs an OAuth token, which needs a
server-side code exchange. Until the backend ships one (below), Discord users
type a name like everyone else — the flow does not block on it.

## Step 7. Chat, always reachable

`Player.svelte` hid its `ChatBubbleOvalLeft` control in `PLAN.md` Step 7 because
it was dead. Restore it as a permanent sibling of the queue toggle.

The `(app)` layout's right dock becomes tabbed — **Queue · N** and **Chat** — so
one dock serves both and the two never fight for the same space. The chat toggle
opens the dock on the Chat tab; the queue toggle opens it on Queue.

Outside a room the Chat tab is not empty and not disabled: it carries the
invitation to start listening together, with Browse rooms and Start a room. That
is the feature's main entry point, which is why the control is always present.

Unread count on the chat toggle while the dock is closed or on the Queue tab,
cleared when the Chat tab is visible.

## Backend asks

1. **`POST /Discord/Token`** — exchange an OAuth `code` for an access token so
   `sdk.commands.authenticate()` resolves. That is the only thing standing
   between the activity and real Discord display names and avatars.
2. **`CreateRoom` accepting `{ name, description }`** — removes the
   create-then-rename race in Step 6 and the moment where a fresh room shows its
   own GUID as its name.
3. **Occupancy** — any of a `users` frame, a count on the room list, or a
   join/leave frame that is not a chat message. The design deliberately ships
   without it; adding it upgrades the lobby from "rooms that exist" to "rooms
   with people in them", which is the single largest improvement available.
4. **Room expiry** — rooms are never deleted, so the lobby only grows. An idle
   TTL, or a flag for "has ever been joined", keeps it usable.

None of 2–4 block the work.

## Verification

- `/rooms` lists rooms from a live socket, filters as you type, and a room
  created in a second tab appears without a refresh.
- Creating a room lands in `/room?id=` with the name you typed already applied.
- `/room?id=<random guid>` shows "That room is gone", not a spinner.
- Two browsers in one room: adding a track updates both queues; play/pause,
  skip, and seek stay within ~0.5s; chat arrives in both including your own.
- Killing the network mid-session reconnects with backoff and posts a fresh join
  notice — expected, and the chat says so.
- The chat control is present on `/`, `/search`, and `/artist`, and opens to the
  invitation state.
- `npm test`, `npm run type-check`, `npm run lint`, `svelte-check`, build.
