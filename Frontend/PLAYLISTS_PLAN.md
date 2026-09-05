# Accounts and playlists: Dom, and the two pages in front of it

> **Status:** all seven phases implemented. Covers both repositories —
> `~/PhpstormProjects/AudioFrontend` (this one) and `~/RiderProjects/AudioAPI`.
>
> Backend proof: `dotnet run --project Dom -- --self-check` passes all five checks, and Dom was
> exercised live over HTTP — register two accounts, create a private playlist, `Mine` shows it and
> `Public` does not, a stranger gets `404`, make it public and an anonymous `GET` gets `200`, upload
> a cover and fetch it back with `Cache-Control: public, max-age=604800`, a stranger's `DELETE` gets
> `404`, the owner's gets `204`, and the cover file is gone with it.
>
> Frontend proof: `npm test` (105), `npm run lint` and `npm run build` all pass.

An account is a username and a password. A playlist is a named, ordered list of tracks that
belongs to one account and is either public or not. A new service — **Dom** — owns both, and is
the first service in the stack that keeps something a restart has to survive.

```
                  /Audio/Download*   Dunav :5341 ─┐    ┌─ Gaida.Pods.YouTube
                  /Audio/Preload*                 │    │
client ─→ proxy ──┼ /Audio/Multiplayer* Selo :5342┼──→ Gaida.API :5340 ─┴─ Gaida.Pods.MusicDatabase
                  ├ /Audio/Accounts*              │
                  ├ /Audio/Playlists*  Dom  :5343 │
                  └ everything else ──────────────┘
```

Dom talks to nobody. It stores IDs and a snapshot of each track's display fields, so a playlist
renders without a fan-out to Gaida on every open.

---

## 1. Design direction

The app already has a visual system and a vocabulary. This feature extends it; it does not bring
its own. No new fonts, no new palette.

### What the existing system already says

| Token / idiom | What it already means | Where |
| --- | --- | --- |
| `--color-gold` `#e8b04b` | **the library** — what the server holds | `/browse`, the folder door in the header, room IDs |
| `--color-primary-*` violet | the app itself, and *live* | logo, room rail when playing, every primary action |
| `--color-ember` `#e85a4b` | something went wrong | form errors |
| `radius-panel 8 → row 6 → art 4` | panels contain rows contain artwork | everywhere |
| `.eyebrow` (JetBrains Mono, 0.68rem, 0.13em) | a structural label | every section head |
| `#room-rail` | **one pixel carries state** — haze / gold / violet | micro-mode player |
| `#player-cover` | cover, full-bleed, 74% dark, 2px blur | micro-mode player |
| `/empty.png` | there is no artwork for this | `imageFallback` in Queue, Song |

Two of those are load-bearing here.

### The signature: the rail

A playlist has exactly one piece of state a glance needs to carry — **public or not** — and this
app already has an idiom for a state that has to be legible in no space at all: `#room-rail`, a
one-pixel line whose colour is the whole message.

Every playlist card takes a 1px rail across its top edge:

- **public** → `--color-primary-0` — the same violet a live room's rail uses
- **private** → `--color-haze` — present, dim, not shouting

No padlock glyph, no "PRIVATE" badge, no second accent colour. The word itself appears once, in
the `.eyebrow` group heading above the cards (`Private · 3`), which is exactly how `/rooms` already
separates *Named* from *Never named*. The rail is the recall; the heading is the teaching.

This is the one accessory. Everything else on a card is quiet.

### The hero: the cover, full-bleed

A playlist page opens with its own cover behind the title — the `#player-cover` treatment lifted
straight out of `app.css`: `object-fit: cover`, a `color-mix(… dark-0 74%)` scrim, `blur(2px)`.
The title sits on it in Unbounded at the same weight `/rooms` uses, with one mono line under it:

```
14 tracks · 51:07 · public
```

That line is a `.font-mono` fact, tabular-nums, the same register as the queue footer's
`12 tracks · 41:20 left`. The page states what it is before it lists anything.

For a playlist with no cover of its own, the hero is the first track's artwork — which is the
same rule the cards use, so nothing special happens at the top of the page.

```
 ┌──────────────────────────────────────────────────┐
 │ ░░░ blurred cover, 74% dark ░░░░░░░░░░░░░░░░░░░░ │
 │  YOUR PLAYLIST                        (eyebrow)  │
 │  Late shift                           (Unbounded)│
 │  14 tracks · 51:07 · public              (mono)  │
 │  [ Play all ] [ Queue all ]  [ Edit ] [ ⋯ ]      │
 └──────────────────────────────────────────────────┘
   1  ▸ ▪ Track                     Artist     3:41  ×
   2  ▸ ▪ Track                     Artist     4:02  ×
```

### Cards

```
 ┏━━━━━━━━━━━━━━━━━┓  ← 1px rail: violet public / haze private
 ┃                 ┃
 ┃   cover (art)   ┃
 ┃                 ┃
 ┠─────────────────┨
 ┃ Late shift      ┃  Golos, semibold, truncate
 ┃ 14 · 51:07      ┃  mono, fog
 ┗━━━━━━━━━━━━━━━━━┛
```

Grid: `repeat(auto-fill, minmax(9.5rem, 1fr))`, `gap-3`, cover at `rounded-art`, card at
`rounded-panel`, `border-haze`, `bg-surface-100` — every value already in the theme.

### Cover resolution, in one place

One rule, one component, three outcomes:

1. a cover the owner uploaded → `GET /Audio/Playlists/{id}/Cover`
2. else the first track's `thumbnailUrl` (already passed through `proxyThumbnails`)
3. else `/empty.png`

`/empty.png` is the app's existing "no artwork" answer and the empty playlist gets it too — the
brief's TODO default already exists. If it later deserves its own image, one file changes.

`PlaylistCover.svelte` owns this and nothing else calls it out of line, so the rule cannot drift
between the grid, the hero and the queue footer.

### The two doors in the header

`/browse` is gold because gold is the library. Playlists are not the library — they are what
people cut out of it — so the playlist door hovers violet, the app's own accent. Same 40px
square, same `border-haze` → accent-on-hover, same `aria-current`-style active treatment the
browse link uses. Icon: `RectangleStack` (confirmed present in `@steeze-ui/heroicons`).

Hidden below `sm:` exactly like the browse door, for the same reason: at 320px the header is a
logo, a field and a face.

### Copy

| Where | Text |
| --- | --- |
| Queue footer button | `Save` → opens an inline name field → `Save playlist` |
| After saving | the footer line becomes `Saved to Late shift` with the name as a link |
| Public list, empty | `No public playlists yet. Make one from your queue and share it.` |
| Your list, signed out | `Sign in to keep playlists.` with `Create an account` beside it |
| Your list, empty | `You have no playlists. Queue a few tracks and save them.` |
| Playlist, empty | `Nothing in this playlist yet. Add tracks from search or the library.` |
| Register conflict | `That username is taken. Pick another.` |
| Login failure | `Wrong username or password.` |
| Visibility toggle | `Make public` / `Make private`; the state reads `public` / `private` |
| Delete | `Delete playlist` → confirm inline: `Delete Late shift?` `Delete` / `Keep` |

Verbs stay the same word from control to confirmation: `Save playlist` → `Playlist saved`,
`Make public` → the mono line reads `public`.

### Quality floor

Responsive to 320px; the grid collapses to two columns and the hero drops the blur (it costs more
than it gives on a phone). Visible `focus-visible:ring-2 ring-primary-500` on every new control,
matching the header. `prefers-reduced-motion` is already handled globally in `app.css` and this
feature adds no animation of its own — the rail does not pulse.

---

## 2. Dom (`~/RiderProjects/AudioAPI`)

### Shape

Copied from Selo, which is the smallest service in the solution and already the right shape:

```
Dom/
  Dom.csproj          net10.0, Microsoft.NET.Sdk.Web, the four Serilog packages
  Program.cs          Selo's Program.cs minus the WebSocket block and the Gaida HttpClient
  Dockerfile          Selo's Dockerfile with the name changed
  SelfCheck.cs        dotnet run --project Dom -- --self-check
  Store/
    DomStore.cs       load, mutate, flush
    User.cs           Username, PasswordHash, Salt, Tokens[]
    Playlist.cs       Id, Owner, Name, IsPublic, CoverFile, Tracks[], CreatedUtc, UpdatedUtc
    TrackSnapshot.cs  Id, Name, Artist, Album?, Duration, ThumbnailUrl?
  Controllers/
    Accounts.cs
    Playlists.cs
```

Registered in `Gaida.slnx` under `/Services/`, next to Dunav and Selo.

### Storage

One JSON file, `Dom__DataFile` (default `/data/dom.json`), held in memory and rewritten whole
under a single lock: serialise to `dom.json.tmp`, `File.Move(tmp, dom.json, overwrite: true)`.

```
// ponytail: one global lock and a whole-file rewrite. At a few thousand playlists
// this is a sub-millisecond serialise on every write. Split into per-user files, or
// move to SQLite, when a write actually shows up in a trace.
```

This is the first service in the stack with state that must survive a restart, so the compose
volume is a named volume, not a bind of `/tmp` — unlike Dunav, where the opposite was deliberate.

### Passwords

The brief sends "one hashed password" from the client. Worth being plain about what that buys:
**a client-side hash is not a security boundary.** Whatever the browser sends is, for all
practical purposes, the password — an attacker with the wire or the database replays it directly.
So the client may hash if it likes, but the server hashes what it receives regardless:

```csharp
// 16-byte salt, PBKDF2-SHA256, 210_000 iterations, 32-byte output — stdlib, no package.
var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
```

Verification uses `CryptographicOperations.FixedTimeEquals`.

Accepted for now, and worth writing down rather than discovering later: no rate limiting on
`/Register` or `/Login`, no password recovery (a lost password is a lost account), tokens in
`localStorage`. The one thing that is not optional is TLS — every one of these endpoints sends a
password or a bearer token in clear text below it. They must never be reachable over plain HTTP.

### Tokens

32 random bytes, base64url, `RandomNumberGenerator.GetBytes`. Stored on the user with an expiry
(30 days, sliding on use). Sent as `Authorization: Bearer <token>`. No JWT, no package.

A small `[Authorize]`-shaped filter or a `TryGetUser(HttpContext)` helper on the store — one
method, both controllers use it.

### Endpoints

All under `/Audio`, matching every other public path.

```
POST   /Audio/Accounts/Register   {username, password}    → 201 {username, token}
POST   /Audio/Accounts/Login      {username, password}    → 200 {username, token}
GET    /Audio/Accounts/Me         Bearer                  → 200 {username}
POST   /Audio/Accounts/Logout     Bearer                  → 204

GET    /Audio/Playlists/Public                            → 200 PlaylistSummary[]
GET    /Audio/Playlists/Mine      Bearer                  → 200 PlaylistSummary[]
GET    /Audio/Playlists/{id}      Bearer?                 → 200 Playlist
POST   /Audio/Playlists           Bearer  {name, isPublic, tracks[]}  → 201 Playlist
PATCH  /Audio/Playlists/{id}      Bearer  {name?, isPublic?, tracks?} → 200 Playlist
DELETE /Audio/Playlists/{id}      Bearer                  → 204
PUT    /Audio/Playlists/{id}/Cover Bearer  multipart      → 200 {coverUrl}
GET    /Audio/Playlists/{id}/Cover                        → image, or 404
```

Errors use the existing envelope, `{"error":{"code":"…","message":"…"}}`, with codes
`username_taken`, `invalid_credentials`, `unauthorized`, `not_found`, `invalid_request`.

`PlaylistSummary` is what a card needs and nothing more:

```json
{
  "id": "p_9f31…", "name": "Late shift", "owner": "kris", "isPublic": true,
  "trackCount": 14, "duration": "00:51:07",
  "coverUrl": "https://api.gergov.bg/Audio/Playlists/p_9f31…/Cover",
  "firstTrackThumbnailUrl": "https://…"
}
```

`coverUrl` is null when nothing was uploaded; `firstTrackThumbnailUrl` is null when the playlist
is empty. The frontend's three-way rule reads exactly these two fields.

A private playlist returns `404`, not `403`, to anyone who is not its owner — a 403 confirms it
exists.

### Covers

Multipart, PNG/JPEG/WebP, 2 MB cap (nginx's `client_max_body_size 10m` already allows it), written
to `Dom__CoverDir` as `{id}.{ext}`. Served from Dom's own origin with
`Cache-Control: public, max-age=604800` — the same treatment `/Audio/Cover` gets, and for the same
reason: inside the Discord activity, the API's origin is the only image host that is mapped.

### Infrastructure

- `compose.yaml`: `dom`, `127.0.0.1:5343:8080`, `Dom__DataFile: /data/dom.json`,
  `Dom__CoverDir: /covers`, named volume `dom-data`. Mirror into `compose.example.yaml`.
- `nginx.example.conf`: two locations, `/Audio/Accounts` and `/Audio/Playlists`, both
  `proxy_pass http://dom` on the same 90s timeouts the plain `/Audio` block uses. No
  `add_header` — same reason the file already documents.
- `API.md`: a new section, written in the same register as the rest.

### Self-check

`dotnet run --project Dom -- --self-check`, in the style of Dunav's: register → login → wrong
password rejected → create a playlist → it appears in `Mine` and not in `Public` → make it public
→ it appears in `Public` → a second account cannot `GET` the first one's private playlist. Round
-trip the store through a temp file and assert it reloads identical.

---

## 3. Frontend (this repository)

### New files

```
src/requests/accounts.ts               register, login, me, logout
src/requests/playlists.ts              the CRUD above, and the cover upload
src/state/account.svelte.ts            token + username, localStorage 'musicrain.token'
src/state/playlists.svelte.ts          mine[], public[], and the mutations
src/components/playlist/PlaylistCover.svelte   the three-way rule, one place
src/components/playlist/PlaylistCard.svelte    cover + rail + name + mono facts
src/routes/(app)/playlists/+page.svelte  +page.ts
src/routes/(app)/playlist/+page.svelte   (no +page.ts — the token is browser-only)
```

`/playlist?id=` rather than `/playlist/[id]`, matching `/room?id=` — the static adapter's
`fallback: index.html` is why that convention exists.

### Changed files

| File | Change |
| --- | --- |
| `components/header/Header.svelte` | the playlists door beside the browse door; the name popover gained an account section below the name field |
| `components/queue/Queue.svelte` | `Save` in the footer, beside Shuffle and Clear |
| `state/user.svelte.ts` | on sign-in, adopt the account name for rooms — one call to `choose()` |

### Accounts live in the popover, not on a route

The header avatar already opens a popover with a form in it. Signed out, it gains two more fields
and two buttons; signed in, it shows the username, a link to your playlists, and `Sign out`. It
fits in the 18rem the popover already is, and it means no `/login` route, no redirect handling,
and no "where do I go back to" question.

The room identity in `user.svelte.ts` stays what it is — a name for a socket, chosen per browser
profile. Signing in sets it; the two are not merged. Discord's `adopt()` still wins inside the
activity.

### Saving the queue

The footer currently reads `12 tracks · 41:20 left · [Shuffle] [Clear]`. `Save` joins them. It
does not open a modal — the dock is 380px wide and a modal over a dock is the wrong shape. The
press swaps the footer row for a name field pre-filled with something usable (`Queue · 4 Sep`) and
a `Save playlist` button; escape or blur puts the row back.

The payload is `queue.items` mapped to `TrackSnapshot`, which is a field-for-field subset of
`SearchResult` — the mapping is one function and is the thing worth a unit test.

In a room, the queue is the server's, not yours. Saving still works — it snapshots what is
playing right now — so unlike `Clear`, the button is not hidden when `session.inRoom`.

### Adding to a playlist from elsewhere

Out of scope for this pass. `SearchRow` and `Song` already carry play/queue actions, and a third
"add to playlist" affordance on every row is a menu this feature does not need yet: the queue is
the way in. Revisit once playlists exist and the gap is real.

---

## 4. Order of work

Each phase is independently shippable and leaves the app working.

1. ~~**Dom skeleton + accounts.**~~ **Done.**
2. ~~**Playlists in Dom.**~~ **Done.** `Playlist.cs`, the store's CRUD, `Controllers/Playlists.cs`,
   `API.md`, and a fifth self-check covering ownership and visibility.
3. ~~**Account panel.**~~ **Done.** `accounts.ts`, `account.svelte.ts`, the header popover's second
   half, and the `user.choose()` bridge.
4. ~~**The playlists page.**~~ **Done.** `playlists.ts`, `playlists.svelte.ts`, `PlaylistCover`,
   `PlaylistCard`, the grid, the header door.
5. ~~**The playlist page.**~~ **Done.** Hero, track list, drag reorder, remove, rename, visibility
   toggle, delete, play all, queue all.
6. ~~**Save the queue.**~~ **Done.** The footer control and the snapshot mapping.
7. ~~**Cover upload.**~~ **Done.** `PUT`/`GET .../Cover`, `Dom__CoverDir`, the compose volume, and
   the picker on the playlist page.

Tests: `playlists.svelte.test.ts` for the cover-resolution rule and the queue → snapshot mapping;
`account.svelte.test.ts` for token persistence and the room-identity bridge. Backend proof is the
self-check, matching Dunav and Selo.

### Where the build differs from the plan above

- **`coverUrl` is a path, not an absolute URL.** Dom sends `/Audio/Playlists/{id}/Cover` and the
  client joins it to its own base. An absolute `api.gergov.bg` URL is unreachable inside the Discord
  activity, where only the `/.proxy` mapping exists — the one thing the absolute form was meant to
  fix is the thing it would break.
- **No `+page.ts` for `/playlist`.** The bearer token lives in `localStorage`, so the fetch belongs
  where the account state is, the same way `/room` reads its own id. `/playlists` keeps its load
  function for the public list, which needs no token.
- **The queue's `dragStart`/`drop` pair did not transfer.** `setNext` moves a track to *next*, which
  is the queue's meaning, not a playlist's — the page moves index to index instead.
- **`queue.replaceWith()` is new.** Play all needed one verb, in the queue, that knows a room queue
  belongs to the server and appends rather than replacing.
- **`eslint.config.js` gained two files** in the `no-navigation-without-resolve` exception list, for
  the same reason the room and artist links are already there: `resolve()` takes a route, not a
  query string.

---

## 5. Open questions

1. **Registration open to anyone?** As written, yes, and with no rate limit that is a spam
   surface on a public host. An invite code checked against an env var is roughly six lines if
   the answer is "not really".
2. **Does a public playlist need a URL you can send someone who is not signed in?** The plan
   assumes yes — `GET /Audio/Playlists/{id}` needs no bearer for a public one — but nothing in
   the UI shares a link yet.
3. **Track snapshots go stale** if a track is retagged in the library. Re-resolving through
   `FindQueryType` when a playlist is opened for editing is the cheap fix; doing it on every read
   is not.
4. **Playlists and rooms.** A room queue that can be loaded from a playlist is the obvious next
   thing and is deliberately not in this plan: it needs a Selo command, which is a protocol
   change.
