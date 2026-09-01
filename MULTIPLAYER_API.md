# Gaida Multiplayer API — frontend implementation guide

Everything a frontend needs to drive `Gaida.API/Controllers/Multiplayer.cs` and the code it delegates to
(`Multiplayer/MultiplayerManager.cs`, `Multiplayer/Room.cs`, `Multiplayer/User.cs`,
`Multiplayer/Handlers/{UserStore,MessageQueue,VirtualPlayer}.cs`, `Controllers/Helpers/WebSocketTextReader.cs`).

Base URL: same host as the rest of the API (production `https://api.gergov.bg`; websockets `wss://api.gergov.bg`).
For the non-multiplayer endpoints (search, resolve, audio download) see `API.md` — this document does not repeat them.

There is **no authentication** anywhere in this feature. Rooms live in process memory, are never deleted, and
are lost on restart.

---

## 1. Endpoints

| Method | Path | Kind | Purpose |
| --- | --- | --- | --- |
| `POST` | `/Audio/Multiplayer/CreateRoom` | JSON | Create a room, returns it |
| `GET` | `/Audio/Multiplayer/Rooms` | WebSocket | Live room-list feed (server push only) |
| `GET` | `/Audio/Multiplayer/Join?room={guid}&username={name}` | WebSocket | Join a room and speak the session protocol |

### 1.1 `POST /Audio/Multiplayer/CreateRoom`

No request body, no parameters. Always `200`.

```json
{ "roomID": "0f0f4e0c-1f8e-4a9d-9b3f-2f6b8a0d1234", "name": "0f0f4e0c-1f8e-4a9d-9b3f-2f6b8a0d1234", "description": "" }
```

A new room's `name` defaults to its own GUID and `description` to `""`. Rename via the `updateroom`
session message (§3.9), not over HTTP.

### 1.2 `GET /Audio/Multiplayer/Rooms` (WebSocket)

Upgrade required — a plain HTTP GET returns `400`.

The server pushes one text frame containing the **full room array** immediately on connect, and again every
time a room is created or its name/description changes. There is no polling and no delta format.

```json
[
  { "roomID": "0f0f…", "name": "Listening Party", "description": "" },
  { "roomID": "6a21…", "name": "6a21…", "description": "friday mix" }
]
```

The server **never reads** from this socket — do not send anything on it. Close it from the client side when
you navigate away; the server tears down its subscription when the request aborts.

### 1.3 `GET /Audio/Multiplayer/Join` (WebSocket)

| Param | Required | Notes |
| --- | --- | --- |
| `room` | yes | Room GUID. Non-GUID → `400` before the upgrade. |
| `username` | no | Display name used in chat and join/leave notices. |

Not a websocket request → `400`. A **well-formed but unknown** `room` GUID is different: the socket is
accepted, then immediately closed with `NormalClosure` and no messages. Treat "socket closed with zero
frames received" as "room does not exist".

`username` is applied **only when the connection is first registered**; changing it later requires a new
connection. When omitted, the server names you `Anonymous <connectionId>` (the ASP.NET trace identifier up
to its `:`), e.g. `Anonymous 0HNAPMH35FQ6R`.

**Identity is per-connection.** The user key is the connection's trace identifier, so a reconnect is a brand
new user — the old one is removed from the room and a new join notice is broadcast. There is no session
resume, no user list message, and no user IDs on the wire.

---

## 2. Wire format

Both sockets are **UTF-8 text frames**. The server reassembles fragmented frames, so you may send messages of
any size; binary frames are ignored.

Client → server messages in the Join socket are plain space-delimited commands:

```
<command>[ <argument…>]
```

Only the **first** space is a separator; everything after it (including that space) is the argument. Numeric
arguments tolerate the leading space; free-text arguments **keep it**, which leaks into some responses — see
the quirks in §5.

Unknown commands are silently ignored. Malformed arguments (non-numeric index, unresolvable ID) are silently
ignored — there is no error frame, ever.

Server → client messages in the Join socket are prefixed strings, dispatched on the first token:

| Prefix | Payload | Scope |
| --- | --- | --- |
| `queue ` | JSON array of queue items | broadcast |
| `current ` | integer index | broadcast |
| `playing ` | `True` or `False` (capitalised .NET bool) | broadcast |
| `seek ` | `<seconds> <serverUtcMs>` | broadcast, or to one user on join |
| `stop` | — (no payload) | broadcast |
| `chat ` | `<username> %% <text>` | broadcast |
| `room name ` / `room description ` | new value | **sender only** |
| `sync ` | `<seconds> <serverUtcMs>` | **sender only** |

Parse by longest prefix: `room name`/`room description` are two-token prefixes, and `sync` is distinct from
`seek` even though both carry a time.

### 2.0 The stamp on `seek` and `sync`

Every frame that moves the shared clock carries a second field: `serverUtcMs`, the server's
`DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` read at the same instant as the position.
**Split on whitespace — do not `Number()` the whole argument**, which yields `NaN`.

```js
const [seconds, stamp] = argument.trim().split(/\s+/);
```

It exists because the position is measured when the server broadcasts and is stale by one downlink
when it lands. Half a round trip is the usual stand-in for that, but the useful round-trip estimate
is a *minimum* over recent samples — the best the link has managed lately — and any given broadcast
is under no obligation to be that trip. A `seek` that spent 300 ms queued was being placed 60 ms
back, and the client landed a quarter of a second ahead of the room at the one moment everybody is
listening for it. With the stamp:

```
flight = (clientNow + skew) − serverUtcMs        // skew: server UTC minus the client's clock
```

`skew` is NTP's offset from three timestamps: send a `sync` at `t1`, read `serverUtcMs` out of the
reply that lands at `t4`, and `skew = serverUtcMs − (t1 + t4) / 2`. Hold it against a **monotonic**
client clock (`performance.now()`), not `Date.now()` — the wall clock steps when the OS corrects it,
and a step mid-track is indistinguishable from the room having moved.

What the stamp does **not** fix is a path whose asymmetry is constant. A link that is always 180 ms
up and 20 ms down biases `skew` by half the difference, and the flight then reads back as exactly
half the round trip again. That is NTP's floor: three timestamps cannot separate a clock offset from
a lopsided path.

### 2.1 Queue item shape

`queue` carries the raw platform result, serialised camelCase — **not** the `SearchResultDto` that `/Audio/Search`
returns. There is no `contentUrl` here:

```json
[
  {
    "id": "audio://qustone--Zm",
    "name": "Stone Cold Crazy",
    "artist": "Queen",
    "album": "Sheer Heart Attack",
    "duration": "00:02:12.5000000",
    "thumbnailUrl": null,
    "originalTitle": null,
    "originalArtist": null
  }
]
```

- `id` is always present. `name`, `artist`, `album`, `thumbnailUrl`, `originalTitle`, `originalArtist` may be `null`
  (search results are normalised to "Unknown title"/"Unknown artist", queue items are **not**).
- `duration` is a .NET `TimeSpan` string (`hh:mm:ss.fffffff`, and `d.hh:mm:ss` past 24h).
- `originalTitle`/`originalArtist` hold the untransliterated title/artist when the platform has one — prefer
  them for display if present.
- To play an item, build the URL yourself from `id`: `/Audio/Download/Opus/112?id={encodeURIComponent(id)}`
  (or `/Audio/DownloadRaw?id=…`). See `API.md`.

---

## 3. Client → server commands

All are sent on the Join socket.

### 3.1 `add <id>`
Resolves the ID through the audio manager and appends it to the queue. Accepts any platform ID
(`audio://…`, `yt://…`). Not a search — pass an `id` from `/Audio/Search` or `/Audio/FindQueryType`; plain
keywords resolve to nothing and are dropped silently. Playlists are not expanded: enqueue each result yourself.
On success everyone receives a fresh `queue …`.

### 3.2 `remove <index>`
Removes the item at that 0-based index. Out-of-range is ignored. If the removed index is **before** the current
one, `current` shifts down implicitly — but only a `queue …` is broadcast, no `current …`. Re-derive the current
item from your last known index after every `queue`.

### 3.3 `setnext <index>`
Moves that item to immediately after the current item. Ignored if out of range or already current. Broadcasts `queue …`.

### 3.4 `skipto <index>`
Jumps to that index. Ignored if out of range or already current. Broadcasts `playing False` then `current <index>`,
and resets the loaded barrier — playback resumes only once every client has sent `loaded` (§4).

### 3.5 `next` / `previous`
Move one item. `next` may move `current` **one past the end** (`current == queue.length`, meaning "nothing playing");
`previous` clamps at 0. Both broadcast `playing False` then `current <n>` and reset the loaded barrier.

### 3.6 `shuffle`
Shuffles the whole list in place, including the current item, and broadcasts `queue …`. `current` is not adjusted,
so the item under the current index changes — clients keep playing what they had until the next `current`/`loaded`
cycle. Follow with `skipto` if you want deterministic behaviour.

### 3.7 `playpause`
Toggles play/pause for everyone. Ignored entirely before the first track has started (no `StartTime` yet).
Broadcasts `seek <seconds> <serverUtcMs>` **then** `playing True|False`, on **both** edges.

The position leads the state change so a client lands on it before it starts moving. Resuming used to
broadcast `playing True` alone, leaving every client to rediscover where the room came back at from
its next `sync` — a whole round trip in the wrong place, on a transition everybody hears.

### 3.8 `stop`
Broadcasts `stop` and marks the session paused. It does **not** clear the queue and does **not** stop the
server's clock — the shared position keeps advancing while everyone is stopped, so a later `sync` (or a
`playpause` resume) returns a position further along than where you halted. Use `playpause` for a real pause;
reserve `stop` for teardown, and re-`seek` after it if position matters.

### 3.9 `updateroom name <value>` / `updateroom description <value>`
Sets the room's name/description and pushes the whole room list to every `/Rooms` subscriber. The confirmation
(`room name <value>`) goes **only to the sender** — other members in the room learn about the rename solely via a
`/Rooms` socket, so open that socket too if you show the room title in-session. Any other key after
`updateroom` is ignored, as is `updateroom <key>` with no value.

### 3.10 `chat <text>`
Broadcasts `chat <username> %% <text>` to everyone including the sender. `%%` is the separator; split on the
**first** ` %% `, since the username comes from an unvalidated query parameter and the text may contain `%%`
itself. System notices arrive on the same channel with the username `System`.

### 3.11 `seek <seconds>`
Moves the shared clock to that position (decimal seconds) and broadcasts `seek <seconds>` to everyone. The
broadcast value is recomputed server-side, so it will differ slightly from what you sent.

### 3.12 `loaded` and `end`
The synchronisation barrier — see §4.

### 3.13 `sync`
Requests the current position. The server replies `sync <seconds> <serverUtcMs>` **to the sender only**. Use
for drift correction and to derive the clock offset (§2.0); it does not touch anyone else's playback.

Replies carry no request id, so keep exactly one in flight — with two outstanding you match a reply to the
wrong send time and time a 600 ms link at 1 ms. Self-clock it: send the next when the last one lands.

---

## 4. Playback synchronisation

The server holds a monotonic clock, not audio. Two counters gate it, each compared against the number of
users currently in the room:

**Loading barrier.** After `skipto`/`next`/`previous`, the clock is cleared and `playing False` is broadcast.
Every client buffers the new track and sends `loaded` exactly once. When the count reaches the number of room
members, the server starts the clock and broadcasts `seek 0` then `playing True`. That pair is your cue to start
audio at position 0.

**Finishing barrier.** When a client's audio ends it sends `end` exactly once. When every member has reported,
the server advances (`playing False`, `current <n+1>`) and the loading barrier starts over.

Both counters count **messages, not distinct users** — a client that sends `loaded` twice releases the barrier
early for everybody. Send each exactly once per track.

Recommended client loop:

```
on "current n"        → load queue[n], do NOT play, then send "loaded"
on "seek t stamp"     → set audio.currentTime = t + flight(stamp)
on "playing True"     → play()      | "playing False" → pause()
on "stop"             → pause()
on audio "ended"      → send "end"
one "sync" in flight  → skew and round trip from the reply (§2.0), correct the position
```

Do **not** compare the raw `sync` reply against your local position and correct past some tolerance. On a
symmetric path a client's own lateness and the reply's staleness are the same size and opposite in sign, so
they cancel: a client a tenth of a second behind the room measures itself as a few milliseconds out and
concludes it is fine. Credit the reply the flight it spent arriving first — that is what §2.0 is for.

Time values are formatted with the server's culture — in practice invariant, i.e. `12.3456789` with a dot.
The stamp is a plain integer. Parse defensively.

---

## 5. Behaviours worth coding around

1. **Leading spaces in free-text arguments.** Because only the first space splits the command, `updateroom name My Room`
   stores the name as `" My Room"` and replies `room name  My Room` (double space); `chat hi` broadcasts
   `chat Alice %%  hi`. `.trim()` names and chat bodies on receipt.
2. **Room names are unvalidated.** No length limit, no sanitising — escape before rendering.
3. **Joining broadcasts before you're ready.** On join you receive, in order: `queue …`, `current …`,
   `playing …`, then `seek …` (only when the queue is non-empty), then the broadcast
   `chat System %% User 'x' joined the session.`. Buffer these; `playing True` can arrive before you have audio loaded.
4. **The last user leaving advances the queue.** Removal is followed by a re-evaluation of both barriers against a
   now-smaller (possibly zero) member count, which can fire the loaded barrier and/or skip to the next track. A
   client rejoining an idle room may find `current` one further along than it left it.
5. **No error frames.** Every invalid command is a silent no-op. Validate client-side; do not wait for a reply.
6. **No user list.** Presence exists only as `chat System %% User 'x' joined/left …` notices. Maintain the roster
   client-side from those if you need one, and rebuild it on reconnect (you will have missed earlier notices).
7. **`current` can point past the end.** Guard `queue[current]` against `undefined`.
8. **Keepalive is 5s** server-side; browsers answer ping frames automatically. Still implement reconnect-with-backoff
   on both sockets — reconnecting the Join socket produces a new identity and a fresh join notice.
9. **CORS** allows `gergov.bg` and its subdomains plus `localhost`/`127.0.0.1`/`::1` on any port. Websocket
   upgrades are not subject to CORS, but `CreateRoom` is.

---

## 6. Minimal flow

```js
// 1. lobby
const lobby = new WebSocket("wss://api.gergov.bg/Audio/Multiplayer/Rooms");
lobby.onmessage = e => renderRooms(JSON.parse(e.data));   // full list, every time

// 2. create
const room = await (await fetch("https://api.gergov.bg/Audio/Multiplayer/CreateRoom", { method: "POST" })).json();

// 3. join
const ws = new WebSocket(`wss://api.gergov.bg/Audio/Multiplayer/Join?room=${room.roomID}&username=${encodeURIComponent(name)}`);
ws.onmessage = e => {
  const raw = e.data, sp = raw.indexOf(" ");
  const cmd = sp === -1 ? raw : raw.slice(0, sp), arg = sp === -1 ? "" : raw.slice(sp + 1);
  switch (cmd) {
    case "queue":   setQueue(JSON.parse(arg)); break;
    case "current": load(Number(arg)); ws.send("loaded"); break;
    case "playing": arg === "True" ? audio.play() : audio.pause(); break;
    case "seek":    { const [t, stamp] = arg.trim().split(/\s+/); seekTo(Number(t), Number(stamp)); break; }
    case "sync":    { const [t, stamp] = arg.trim().split(/\s+/); onSync(Number(t), Number(stamp)); break; }
    case "stop":    audio.pause(); break;
    case "chat":    { const i = arg.indexOf(" %% "); addChat(arg.slice(0, i), arg.slice(i + 4).trim()); break; }
    case "room":    { const j = arg.indexOf(" "); setRoomField(arg.slice(0, j), arg.slice(j + 1).trim()); break; }
  }
};

// 4. queue something found via /Audio/Search
ws.send(`add ${result.id}`);
audio.addEventListener("ended", () => ws.send("end"));
```
