# Plan: fast liveness detection on the room sockets

Goal: a server-side ping that notices a gone client as soon as possible, counting them dead
after 5 seconds without a reply.

**You already have the ping — it just isn't enforced.** `Program.cs:42` sets
`KeepAliveInterval = 5s` but no `KeepAliveTimeout`, so .NET sends the frames and never requires
an answer. Adding the timeout turns it into real liveness detection, and the existing teardown
path already does the right thing when it fires. Both `WebSocketOptions.KeepAliveTimeout` and
`WebSocketAcceptContext.KeepAliveTimeout` were verified to compile on .NET 10.

Protocol Ping/Pong is the right layer here: browsers answer it automatically, so there is no
frontend change, no room timer, and no per-user last-seen state. Kestrel already notices
*graceful* disconnects through `RequestAborted`; the gap is the half-open connection — the
client's network drops with no FIN — which is exactly what ping/pong closes.

## 1. Set the timeout — `Program.cs:42`

```csharp
app.UseWebSockets(new WebSocketOptions
{
    // Ping every second, abort if no pong inside five.
    KeepAliveInterval = TimeSpan.FromSeconds(1),
    KeepAliveTimeout = TimeSpan.FromSeconds(5)
});
```

Worst-case detection is `interval + timeout`, because the clock starts at the *next* ping — 5–6s
here. Shrink the interval, not the timeout, if the ceiling needs to be tighter; the 5s is the
timeout.

## 2. Nothing to change in the teardown — verify this holds

This is why the change is small. On abort:

- the pending `ReceiveAsync` throws `WebSocketException` / `OperationCanceledException`
- `WebSocketTextReader.ReadWholeMessageAsync` already catches exactly those and returns `null`
  (`WebSocketTextReader.cs:63-70`)
- the read loop breaks and the `finally` at `Multiplayer.cs:126` calls `room.RemoveUser(id)`
- `Room.RemoveUser` (`Room.cs:52`) drops them from the store, announces the leave, and re-runs
  `HandleLoaded` / `HandleFinished`

That last step is the real payoff: the barriers count against `Store.Count`, so today a
dead-but-present member stops the room advancing until TCP eventually gives up. This caps that
at ~6s.

## 3. Guard the close — `Multiplayer.cs:122` and `:91`

`await webSocket.CloseAsync(NormalClosure, …)` runs after the loop. On an aborted socket it
throws, and `Join`'s `catch` logs it as an error and **rethrows into an already-hijacked
response**. So:

```csharp
if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
    try { await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None); }
    catch (WebSocketException) { /* peer already gone; the finally still reaps */ }
```

Without this, every keepalive drop becomes a logged error.

## 4. Give the lobby socket a receive loop — `Multiplayer.cs:80`

`HandleRoomUpdateWebSocket` parks on `Task.Delay(Timeout.Infinite, cancellationToken)` and never
calls `ReceiveAsync`. Pongs land in the socket buffer with nothing reading them — **this likely
times out healthy lobby clients**, and it is the one thing in this plan that is not yet
confirmed. Replace the delay with a discard loop, which drains pongs and gets the lobby the same
detection:

```csharp
using var reader = new WebSocketTextReader();
while (await reader.ReadWholeMessageAsync(webSocket, cancellationToken) is not null) { }
```

**Verify before shipping:** connect a client that receives but never sends and assert it survives
past 30s; connect one that goes silent at the TCP level and assert it is gone in ~6s. That single
test settles whether pong processing needs a pending receive.

## What to skip

- **App-level `ping` / `pong` verbs.** Only worth it if something between the server and the
  client strips control frames (nothing normal does), or if RTT should show in the UI. Costs a
  frontend change, a room timer, and per-user state.
- **Reaping on broadcast failure.** `MessageQueue.Add`'s catch sees a dead peer at the next frame,
  which is sooner than the next ping — but it would skip the leave announcement and barrier re-run
  that `Room.RemoveUser` does. The read loop's `finally` follows within milliseconds anyway. Add
  only if broadcasts are measured landing on dead sockets.

## Test

`RecordingWebSocket` already takes a `receiveFailure` exception (`MultiplayerTests.cs:545`) — one
case that throws `WebSocketException` from receive, asserting the user leaves the store and the
barrier releases for everyone else.
