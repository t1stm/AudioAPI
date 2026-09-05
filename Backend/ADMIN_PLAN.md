# Admin panel: `Oko`

> **Status:** all phases implemented and verified. The stack is configured but **not deployed** —
> `ADMIN_USERNAME` and `ADMIN_PASSWORD` in `.env` are still blank, and Oko refuses to start until
> they are set. Name is a placeholder — every other service is a Bulgarian noun (Dunav / Selo / Dom / Gaida), so `Oko`
> ("eye") is offered for the one that watches them.

One private service that shows what the rest of the stack is doing and lets an operator fix it:
Dunav's cache, Dom's users and playlists, Selo's rooms, and the request log of everything.

---

## The one design change worth arguing about

The brief says: *each service opens a persistent WebSocket to the admin panel and reports after
every call.* That is push. It should be **pull** — the admin panel is the client, every service is
the server, exactly as they already are for each other.

Concretely, this is the difference:

| | Push (services connect to Oko) | Pull (Oko connects to services) |
|---|---|---|
| Reconnect / backoff / buffering code | 5 copies, one per service | 1 copy, in Oko |
| New config per service | `ADMIN_URL` + token + retry policy | a token, nothing else |
| Oko is down | every service holds a dead socket and a growing buffer, or drops events silently | nothing happens; nobody notices |
| Oko is slow | backpressure reaches the audio hot path | Oko's problem, alone |
| Cost when nobody is watching | every request does work for an empty room | zero |
| Startup ordering | services want Oko up (the exact dependency [`SERVICE_SPLIT_PLAN.md`](SERVICE_SPLIT_PLAN.md) refused for `/capabilities`) | none |
| Firewall story | Oko must be reachable *from* every service | Oko reaches out; it can stay a leaf |

The push design also inverts the dependency graph the split was built around: today the public
services depend on pods and on nothing else, and Dunav in particular is memory-budgeted down to
`mem_limit: 1g` precisely because an unbounded in-process buffer was the failure it was designed
away from. An unbounded event queue waiting on a dead admin socket puts that back.

Pull loses exactly one thing — the sub-second freshness of a pushed event — and SSE gives that back
without any of the cost above (below).

**So:** every service exposes `GET /Admin/*`. Oko polls the snapshots and subscribes to the live
feeds. Services never learn that Oko exists.

## Transport: native SSE, not WebSockets, not SignalR

.NET 10 ships both halves of this in-box:

- **Server side**, in each service: `TypedResults.ServerSentEvents(IAsyncEnumerable<T>)` over a
  `Channel<T>` reader. A live feed endpoint is roughly ten lines.
- **Client side**, in Oko: `SseParser.Create<T>(stream)` from `System.Net.ServerSentEvents`, in the
  shared framework since .NET 9.
- **Browser side**: `EventSource`, which reconnects on its own with no library and no code.

WebSockets buy bidirectionality that nothing here needs — the feed is server → client only, and
every operator *action* is an ordinary `POST`. SignalR buys transport fallback and hubs for the same
one-way stream, at the cost of a dependency and a JS client bundle. Skip both.

The room feed and the request feed are the only things that want to be live at all; users,
playlists and cache entries are fine on a 2–5 s poll of a JSON snapshot, which is also
self-healing after a dropped connection in a way a stream is not.

## Auth

Two separate boundaries; do not conflate them.

**Operator → Oko: HTTP Basic**, `ADMIN_USERNAME` / `ADMIN_PASSWORD` from the environment. The
browser implements the whole login flow natively — no login page, no session store, no cookie, no
CSRF token, no logout button. One middleware, a `CryptographicOperations.FixedTimeEquals` on both
fields, `WWW-Authenticate: Basic` on failure. It also solves the `EventSource` header problem for
free: `EventSource` cannot set an `Authorization` header, but the browser replays cached Basic
credentials on same-origin requests, so the live feed just works.

> **Security note, and the only place this plan is deliberately not lazy.** Basic sends the password
> on every request, base64 not encrypted. Publishing 5344 on `0.0.0.0` as requested means anyone the
> firewall lets through can also *sniff* it on a hostile network, and this one password reaches every
> account in Dom. Put TLS in front (the existing nginx already terminates it), or reach the panel
> over an SSH tunnel and bind loopback after all. Firewall alone protects who connects, not what the
> connection reveals.

**Oko → services: a shared secret**, `ADMIN_TOKEN`, checked on every `/Admin/*` route with a
fixed-time compare, 404 (not 401) on mismatch so the surface is invisible to a scanner. The Docker
network already keeps these ports off the host, but `/Admin/users` hands out every account in the
system — that is a real trust boundary and it gets a real check, not just network topology.

## Layout

```
                        ┌──────────────────────────────────────────┐
  browser ──Basic──→    │  Oko :5344                               │
     ↑                  │   • static wwwroot/index.html            │
     └──EventSource─────│   • polls  GET  /Admin/snapshot   (2 s)  │
        (merged feed)   │   • streams GET  /Admin/events    (SSE)  │
                        │   • proxies POST /Admin/…       (actions)│
                        └────────────────┬─────────────────────────┘
                                         │ X-Admin-Token
              ┌──────────────┬───────────┼───────────┬──────────────┐
           dunav          selo          dom       gaida-api      pods…
```

Oko holds no state of its own beyond an in-memory audit log. It is a fan-in and a static file
server. If it dies, nothing else notices.

### What runs when nobody is watching

Nothing. Oko has no timer, no background service, no warm cache. The 2 s poll is driven by the open
browser tab, not by Oko — close the tab and Oko stops making requests entirely. Left alone for a
week it is an idle ASP.NET process making zero outbound calls, and the first page load after that
week fans out fresh and shows current state.

That is the point of holding no state: there is nothing to keep warm, so there is no reason to poll.
A cached snapshot would only ever be a staler copy of a dictionary that is already in memory one
hop away.

Over a long idle period the pieces that *are* always-on stay bounded by construction:

| | Idle behaviour |
|---|---|
| Oko's snapshot fan-out | only while a tab is open; nothing retained between page loads |
| `/Admin/events` channels | written only while a subscriber is attached; no subscriber, no allocation |
| `/Admin/requests` ring | always filling, fixed 500 entries (~50 KB), wraps in place — the same size on day 7 as on minute 1, watched or not |
| Oko's audit log | grows only on an operator action, and capped at 1000 entries |

The cost of this is history: open the panel after a week and you see the current state plus the last
500 requests per service, with nothing about the six days in between. Keeping those days is the
time-series question, and the answer to it is Prometheus, not Oko — see [Deliberately
skipped](#deliberately-skipped).

## New code

One new project and one new file per service:

| Where | What | Rough size |
|---|---|---|
| `Gaida Library/Gaida.Admin/` | `AdminApi.cs` — token filter, request ring buffer + middleware, `Channel` broadcast, `MapAdmin(snapshot)` | ~120 lines, one file |
| `Services/Dunav/Admin.cs` | cache snapshot + evict | ~40 |
| `Services/Selo/Admin.cs` | room snapshot + kick / close, live room feed | ~60 |
| `Services/Dom/Admin.cs` | users + playlists snapshot, mutations | ~90 |
| `Services/Gaida.API/Program.cs` | one `MapAdmin` line; the ring buffer is the payload | ~3 |
| `Platforms/Gaida.Pods.MusicDatabase/Program.cs` | library rows + the metadata editor (phase 7) | ~45 |
| `Platforms/Gaida.Pods.*` | same one-liner | ~10 each |
| `Services/Oko/` | Program, targets config, aggregator, audit log, SelfCheck, `wwwroot/index.html` | ~250 + ~350 HTML |

`Gaida.Admin` is the one abstraction this plan adds, and it is added deliberately: Selo and Dom do
not reference `Gaida.Core` today, so the alternative is five hand-copied constant-time token
comparisons that are free to drift apart. A security check that must not drift is worth a project
reference. Everything else in it (the ring buffer, the channel) is there because it is the same code
five times over, not because it might be reused later.

### The shared surface

```
GET  /Admin/snapshot   → service-specific JSON, whatever the operator needs to see
GET  /Admin/requests   → the last N requests: method, path, status, ms, at
GET  /Admin/events     → text/event-stream; requests as they happen, plus service-specific events
POST /Admin/…          → service-specific actions
```

`/Admin/requests` is a fixed-size ring (500 entries, ~50 KB) filled by one middleware, so *every*
service answers "what requests have been made" — Gaida.API is only the one the brief named. Cost per
request is an allocation and a lock-free enqueue; nothing is written to disk and nothing is retained
across a restart. `/Admin/events` writes into its channel only while at least one subscriber is
attached, so an unwatched panel costs literally nothing.

### Per-service payloads

**Dunav** — `CacheService`'s dictionary is already the answer: key, decoded `(codec, bitrate, id)`,
bytes on disk, age, in-flight subscriber count, plus the totals against `Dunav__MaxBytes`. Actions:
evict one key (`CacheService.Forget`, already public), evict all. `HashId` is one-way, so the plain
`id` has to be kept beside the entry to be displayable — one extra field on `CacheEntry`.

**Dom** — the operator view of `DomStore`: per user, name / created / active token count / playlist
count, never the hash or salt. Actions: rename, force-logout (drop tokens), reset password, delete
account; per playlist, rename, toggle public, remove a track, delete. Each is a new `DomStore`
method under the existing `gate` lock, reusing the existing atomic whole-file write — no new
persistence path.

**Selo** — `MultiplayerManager.GetRooms()` with, per room, id / name / description / user list /
current track / queue length. Actions: kick a user (`Room.RemoveUser`, already public), close a room
(`RemoveRoom`). `RoomsChanged` already exists as an event and becomes the live feed's source.

**Gaida.API and the pods** — the request ring, nothing else. Gaida.API holds no cache to show.

Every mutation appends one line to Oko's audit log — who, what, which target, when. Dom's actions
destroy real user data that no cache refills; a delete with no record of it is the thing you regret.
The log is in memory and capped at 1000 entries, so it cannot grow without bound and it goes with a
restart; make it an append-only file the day that matters.

## UI

One static `wwwroot/index.html` in Oko: tabs per service, `fetch` on a timer, one `EventSource` for
the live feed, `<template>` + `<table>`. Vanilla, no build step, no npm, no framework, no bundler in
a service whose entire job is to render six tables. Confirm dialogs on the Dom and Selo destructive
actions.

Explicitly **not** a route in the SvelteKit frontend: that builds to a static bundle deployed to the
public web root, and an admin surface has no business in it.

## Phases

Each phase ends running, and phases 1–2 are the ones that prove the design.

1. **`Gaida.Admin` + Dunav.** ✅ **Done.** Shared library, wired into exactly one service, verified
   with `curl` against a live Dunav and a stub upstream:

   | Checked | Result |
   |---|---|
   | `/Admin/snapshot` with no token / a wrong token, `POST /Admin/evict-all` with none | 404 — surface invisible, not merely refused |
   | `/Admin/snapshot` with the token | 200, cache contents with readable labels (`opus 128k yt://…`) beside the hashed keys |
   | `/Admin/events` open across three requests | three `event: request` frames, one per request, live |
   | `/Admin/requests` | the same three; the `/Admin` calls themselves correctly absent |
   | `POST /Admin/evict?key=…`, then `evict-all` | entry gone, file unlinked, `0` files left on disk; unknown key 404 |
   | Restart with `ADMIN_TOKEN` unset | audio still served, `/Admin/*` 404 even with the token, one log line saying why |

   `dotnet run --project Dunav -- --self-check` also passes its new third assertion: 5000 recorded
   requests leave the ring holding the newest 500.

   Two deviations from the sketch above, both deliberate:

   - The service half lives in `Services/Dunav/Admin.cs`, not `Controllers/Admin.cs` — `MapAdmin`
     hands back a `RouteGroupBuilder` with the token filter already on it, so Dunav's two actions are
     two `MapPost` lines rather than a controller class.
   - **No subscriber count** in the Dunav snapshot. `StreamSpreader` does not track readers, and
     adding a counter means touching a primitive the platform pods also depend on. The snapshot
     answers `pending` / `downloading` / `complete` instead, which covers the operational question.
     Add the counter in `Gaida.Core` if "who is streaming this right now" turns out to be something
     anyone actually asks.
2. **Oko skeleton.** ✅ **Done.** `Services/Oko/` — Basic auth, target list from config, snapshot
   fan-in, static `wwwroot/index.html`. Verified from a browser and by `curl`:

   | Checked | Result |
   |---|---|
   | `/`, `/api/snapshot` with no credentials or a wrong password | 401 with `WWW-Authenticate: Basic realm="Oko"` |
   | right credentials | 200, the page renders every target |
   | one target stopped (`gaida-api`) | the other three still render; the dead one is a red dot and "Not answering — ConnectionError" |
   | started with no `ADMIN_USERNAME`/`ADMIN_PASSWORD` | refuses to boot rather than serve an open panel |

   `--self-check` covers Basic parsing in 11 cases (wrong username with the right password, a
   password containing the separator, every malformed header shape) and the fan-in with one target
   refusing and one unreachable.

3. **Selo, Dom, Gaida.API, pods.** ✅ **Done.** Read-only snapshots and request rings everywhere.
   (`gaida-local` later grew an editable surface of its own — see phase 7.)
   Verified live: Dunav's four cache entries with labels and sizes, Selo's two rooms with listeners
   and queue state, Dom's two accounts and two playlists. Dom's snapshot carries no hash, no salt,
   no iteration count and no token value — checked by grep, not by reading the code.

   Two notes from doing it:

   - `Room.Snapshot()` reads under the player's existing `SemaphoreSlim`, not beside it. `Items` is a
     plain `List<T>`; indexing it while another socket removes a track is how a monitoring endpoint
     takes a room down. `MapAdmin` therefore gained an async overload.
   - `Room.Snapshot()` reads `User.Username`, never `User.ChatUsername` — the latter assigns an
     anonymous name as a side effect, and an admin read must not change what a room calls people.
4. **Mutations + audit log.** ✅ **Done.** Every action driven through Oko against live services:

   | Checked | Result |
   |---|---|
   | Dunav evict / evict-all / unknown key | entry gone, file unlinked; 404 for a key that is not there |
   | playlist rename, visibility, remove track, delete | applied; removing track 99 of 2 is a 400 with the reason |
   | user rename | applied, **and the account's playlists move with it** — `Playlist.Owner` is the display name, so a rename that ignored them would orphan every one |
   | rename onto a taken name | 400 "That username is taken." |
   | reset password | old token 401s, old password 401s, new password 200s — the reset revokes every session, because each was issued against the old password |
   | short password | 400, and the attempt is logged with the value redacted |
   | sign-out, delete account | token dies; delete takes the account's playlists and their cover files with it |
   | Selo kick, with two real WebSocket listeners | kicked member's socket closed by the server, the other stays connected |
   | Selo close-room | every listener disconnected, room gone, closing it again 404s |
   | unknown target / unknown action | 404 from Oko / 404 from the service |
   | audit log | all 22 attempts recorded with who, what and the result — failures included; `newpassword123` appears nowhere |

   **One real bug found by the WebSocket test, which no amount of `curl` would have shown.** The
   kick worked — member removed, socket closed — but the operator got a 502 five seconds later.
   `WebSocket.CloseAsync` sends the close frame *and waits for the peer's acknowledgement*, and that
   acknowledgement is consumed by the member's own receive loop already sitting in
   `ReadWholeMessageAsync`. The wait never completes. `CloseOutputAsync` sends the frame without
   waiting, which is all a kick needs; the receive loop tears the connection down through its own
   `finally`. An action that succeeds while reporting failure is the worst shape of bug for a panel
   whose entire job is telling you what happened.

   Notes on shape:

   - The `DomStore` admin methods are owner-agnostic, sitting beside the owner-scoped ones rather
     than replacing them: everything the public API does asks "does this caller own it", and an
     operator owns nothing.
   - Oko's action route is a generic `POST /api/action/{target}/{action}`, and it is safe because of
     what it cannot reach — `{action}` is one route segment, so it names a route the service already
     chose to expose. Oko does not know what any action means, which is the point.
   - The audit log redacts any parameter whose name looks like a secret. `reset-password` carries the
     new password, and a log that recorded it would turn the safety feature into the place passwords
     accumulate. `--self-check` asserts this rather than trusting it.
5. **Live feed end to end.** ✅ **Done.** Service `Channel` → Oko `SseParser` → merged browser
   `EventSource`. Verified: with one browser subscribed, traffic to Dunav, Selo and Dom arrived as
   three tagged frames on one stream. The upstream connections open on subscribe and close on
   disconnect, so the idle cost stays zero; a target that drops is retried every 5 s while a
   subscriber is still there, which makes a restart a gap in the feed rather than the end of it.

   The browser page holds the same rule: `document.hidden` suspends both the poll and the feed, so a
   backgrounded tab costs nothing. (It also means a panel opened in a background tab shows its shell
   and no data until it is focused — working as designed, and worth knowing before it looks like a
   bug.)
6. **Compose, `.env`, docs.** ✅ **Done.**

   | Where | What |
   |---|---|
   | `Services/Oko/Dockerfile` | no project reference to anything, so this image does not rebuild when a service changes |
   | `compose.yaml` | `ADMIN_TOKEN` on the shared `x-service` anchor, so all seven services get it from one line; an `oko` service with all seven targets |
   | `compose.yaml` ports | `"${OKO_PORT:-5344}:8080"` — the one entry here **not** bound to `127.0.0.1`, as asked |
   | `.env` | `ADMIN_USERNAME` / `ADMIN_PASSWORD` blank for the operator to fill; `ADMIN_TOKEN` generated; `OKO_PORT` |
   | `nginx.example.conf` | a commented TLS vhost for the panel, plus the SSH-tunnel alternative |
   | `API.md` | a closing section saying `/Admin/*` is **not** part of the public API and why |

   Verified on the built image, without touching the running stack:

   | Checked | Result |
   |---|---|
   | `docker compose config` | parses; all seven targets resolve to container DNS names; port 5344 published |
   | `docker compose build oko` | builds |
   | `docker run` with no credentials | logs the fatal line and exits — a blank password is a crash-loop, never an open door |
   | `docker run` with credentials | `401` bare, `200` with them, 25 KB page — `wwwroot` is published into the image |

   Two things deliberately left for a human:

   - **Nothing was deployed.** `docker compose up -d` restarts live services, which is not a change
     to make on someone's behalf. The stack currently running predates all of this, so its `/Admin`
     routes are still absent and Oko is not up.
   - **`ADMIN_PASSWORD` is blank.** Inventing a password for someone is worse than leaving the
     service down and loud about why.

   The `ADMIN_TOKEN` in `.env` *was* generated, on the grounds that it is machine-to-machine and
   nobody types it. Rotating it is replacing the value and running `docker compose up -d`; every
   service reads the same one.

Gaida.Bot is out of scope — it is not in `compose.yaml` and has no state an operator would edit. It
can take `MapAdmin` in an afternoon later if it ever wants to.

### Self-checks

The stack's convention is `dotnet run --project <name> -- --self-check`, and this plan keeps it.
Oko gets a `SelfCheck.cs` covering the two pieces of non-trivial logic it owns: Basic parsing
(valid, malformed, wrong password all resolve correctly) and snapshot fan-in with one target
unreachable — a down service must render as "down" and must not fail the other five. `Gaida.Admin`'s
ring buffer gets one assertion that it wraps at capacity instead of growing.

### 7. Editing the library's metadata — `gaida-local`

✅ **Done.** The one pod that owns state worth editing: the names and albums in the music database.
Titles and artists are **variant lists**, not single strings (see
[`MUSICDB_FORMAT_PLAN.md`](MUSICDB_FORMAT_PLAN.md)), and the editor edits the whole list — the point
of it is the alternates that the ordinary API flattens down to one display name.

| Where | What |
|---|---|
| `MusicManager.Find` | plain substring matching over every variant, the album and the path |
| `MusicManager.EditAsync` | replaces titles / artists / album, saves the folder's `Info.json` |
| `MusicManager.Summary` | songs, folders, how many lack an album or an artist |
| `GET /Admin/library?q=&take=` | the rows, every variant included |
| `POST /Admin/edit-song?id=&title=…&title=…&artist=…&album=` | repeated parameters are the list, in order |
| `GET /api/read/{target}/{action}` | Oko's read proxy — forwards like the action proxy, but is **not** audited |
| Panel "Library" tab | search, then an inline editor: one variant per line, album, Save |

Four decisions worth keeping:

- **Substring, not `SearchByTerm`.** The fuzzy search is tuned for a listener who half-remembers a
  title. An operator fixing `Оркестър Имперал` needs to find *that* typo, and a search that also
  helpfully returns the correctly spelled song is a search that hides what is being looked for.
- **The ID is never regenerated**, though it is derived from exactly the fields being edited
  (`UpdateRandomId`). It is the handle every playlist snapshot, Dunav cache key and shared link
  already holds; re-rolling it on a typo fix would orphan all of them. `RereadTags` regenerates
  because a bulk migration has no such links to keep.
- **What the operator types is taken literally.** The import path adds a romanization of every
  value; this does not. A person editing a name is the authority on it, and should not find a line
  they never wrote appearing underneath. Extra variants are welcome — as extra lines.
- **A missing parameter leaves a field alone; an empty one clears it.** `album=` clears the album,
  no `album` at all keeps it. That is why the route reads the query directly instead of binding it:
  model binding gives "absent" and "sent empty" both as an empty array.

The trap this uncovered, and the reason `MusicInfo.StoredCoverUrl` now exists: `Load` rewrites
`$[DOMAIN]` in `CoverUrl` into the real host, and the substitution used to be one-way. That was
harmless while only the loader wrote `Info.json` — it writes entries it has not substituted yet — but
an admin edit saves an entry that *has* been, which would have baked this host's domain into the
library file and broken every cover the next time `DOMAIN` changed. One serialization property fixes
it for every writer instead of leaving a rule to remember.

Verified against a live pod and a throwaway library:

| Checked | Result |
|---|---|
| search by artist, by an exact Cyrillic typo, by nonsense | 2 rows, 1 row, 0 rows |
| edit a title and add an artist variant | both variant lists saved in order, album untouched |
| fix `Оркестър Имперал` → `Оркестър Империал`, set an album | applied |
| `album=` empty | cleared |
| edit down to no titles / unknown id | 400 with the reason / 404 |
| `Info.json` on disk | new names present, `$[DOMAIN]` kept, no `music.example.com` anywhere |
| **restart the pod** | every edit still there — the file is the record, not the process |
| audit log | the four edits recorded; the library *reads* absent, which is why reads have their own proxy |
| the panel itself (rendered in jsdom against live Oko) | Library tab appears only for `gaida-local`, editor pre-fills one variant per line, Save round-trips and the row updates in place |

The pod's `--self-check` covers the two silent ones: an edit must not re-roll the ID, and the saved
file must keep the placeholder.

Not done, because nothing asked for it: renaming the *files and folders* on disk. This edits the
database only, so `Оркестър Имперал/` stays the folder name after its artist is fixed. Add it when
the folder names start mattering to something other than the eye.

## One ASP.NET trap, written down because phase 4 will meet it again

An expression-bodied `async` lambda taking `HttpContext` has the natural type
`Func<HttpContext, Task>` — which *is* `RequestDelegate`. ASP.NET then invokes it and throws the
returned `IResult` away, so the endpoint answers **200 with an empty body and no content type**: it
looks healthy from every angle except the one that matters. It cost an hour here.

```csharp
app.MapGet("/api/snapshot", async (HttpContext http) => Results.Json(await Thing(http)));   // 200, empty
app.MapGet("/api/snapshot", (CancellationToken token) => ThingAsync(token));                // correct
app.MapGet("/api/x", async Task<IResult> (CancellationToken t) => { … return Results.Json(v); });
```

Return the value and let minimal APIs serialise it, or spell out `async Task<IResult>`.
`CancellationToken` binds to `RequestAborted` on its own, so `HttpContext` is rarely needed at all.

## Deliberately skipped

| Skipped | Why | Add when |
|---|---|---|
| Prometheus + Grafana, OpenTelemetry, .NET Aspire dashboard | This panel answers "what is in the cache right now" and "delete this room" — inventory and moderation, not time series. Those tools answer the other question and answer it far better; installing them for this one would leave both jobs half done. | You want history, graphs over time, or alerting. Then add them *beside* Oko, not instead of it. |
| `MapHealthChecks` / `AspNetCore.Diagnostics.HealthChecks` + its UI | Oko's snapshot poll already is the liveness check: a service that does not answer renders as down. A second endpoint saying the same thing is a second endpoint. | Something other than Oko needs to ask (an orchestrator, a load balancer, an uptime probe). |
| A database, or any persistence in Oko | Every source of truth already lives in the service that owns it. Oko caching it would only create a second, staler copy. The audit log is in memory and goes with a restart. | The audit log has to survive a restart — then it is one append-only file, not a database. |
| Per-user admin accounts, roles, sessions, JWT | One operator, one password, from the brief. | There is a second operator who should not be able to delete accounts. |
| Push from services / WebSockets / SignalR | See the top of this document. | A service needs to *ask Oko something*, which would make it genuinely bidirectional. Nothing here does. |
| Rate limiting on `/Admin/*` | It is behind a firewall, a token, and a password. | It is ever exposed publicly, which it should not be. |

## Sources

- [Server-Sent Events in ASP.NET Core and .NET 10](https://milanjovanovic.tech/blog/server-sent-events-in-aspnetcore-and-dotnet-10) — `TypedResults.ServerSentEvents` over a `Channel`.
- [You Probably Don't Need SignalR in .NET 10](https://systemshogun.com/p/you-probably-dont-need-signalr-in) — the one-way-stream case against a hub dependency.
- [`SseParser` (System.Net.ServerSentEvents)](https://learn.microsoft.com/en-us/dotnet/api/system.net.serversentevents.sseparser?view=net-10.0) — the in-box client parser Oko consumes with.
- [Implementing SSE vs. WebSockets vs. Long Polling in ASP.NET Core](https://developersvoice.com/blog/dotnet/sse-websockets-longpolling-aspnet-core/) — transport comparison for dashboards specifically.
- [Push vs Pull Based Architecture](https://dev.to/nk_sk_6f24fdd730188b284bf/system-design-trade-off-push-vs-pull-based-architecture-lej) — the connection-ownership trade-off argued above.
- [Health checks in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0) — what is being skipped, and its authorization story.
- [WebSockets support in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets?view=aspnetcore-10.0) — for the Selo comparison; Selo already uses raw `WebSocket`.
