# Ping compensation and the shared clock

Companion to [`MULTIPLAYER_PLAN.md`](./MULTIPLAYER_PLAN.md), which built the room
and left everyone in it roughly a third of a second apart. This file is about
closing that to ten milliseconds without anyone hearing it happen.

Source of truth for the protocol is `~/RiderProjects/AudioAPI/MULTIPLAYER_API.md`.
Everything measured below was measured against a real `Gaida.API` on
`localhost:5226` with `tools/roomSim.mjs`, not modelled.

---

## 1. What is actually wrong

The room's position lives on the server as a `Stopwatch`. `sync` is the only way
to read it, and today `session.svelte.ts` compares the reply against the local
position and corrects only past half a second:

```ts
if (Math.abs(audio.currentSeconds - reported) <= driftToleranceSeconds) return;
```

Two things are wrong with that, and the second is the interesting one.

**The tolerance never fires.** Across a 95-second scripted session with four
clients between 10 ms and 600 ms of ping, the correction fired **zero times**
while the room sat a third of a second apart. Half a second is not a tolerance,
it is an off switch.

**The comparison is close to blind.** The reply describes the server one return
trip ago. A client is also *behind* the room by roughly one downlink, because
that is how late `seek 0` and `playing True` reached it at the top of the track.
On a symmetric path those two errors are the same size and opposite in sign, so
they cancel: a client 90 ms behind the room measures itself as 1–6 ms out and
concludes it is fine. Measured directly — three clients, real spread 111 ms:

| client | ping | real offset from the room | what `sync` told it |
| ------ | ---- | ------------------------- | ------------------- |
| LAN    | 10 ms  | 31 ms behind  | −1 ms  |
| Home   | 60 ms  | ~0 ms         | −6 ms  |
| Mobile | 180 ms | 78 ms behind  | −26 ms |

Raising the tolerance would not have helped. The signal itself has to be fixed
first, and that is what the whole feature is.

---

## 2. The fix, in one line

```
error = reported + rtt / 2 − localPosition
```

`reported + rtt/2` is the server's position **at the moment the reply landed**,
which is the only quantity the local position can honestly be compared against.
Everything else in this plan is defending that number against jitter and then
spending it carefully.

### The loop

Landed in [`src/lib/syncClock.ts`](./src/lib/syncClock.ts) — pure, no DOM, no
runes, so the simulator and the browser run the identical estimator.

| piece | what it does | why |
| ----- | ------------ | --- |
| `sample()` | records `reported + rtt/2 − position` and the trip that carried it | the reply is stale by exactly one return trip |
| `error` | takes the **lowest-RTT** sample of the last 16, never the newest or the mean | a round trip can only be inflated, never shortened, so the fastest one seen is where the symmetry assumption is least wrong. Averaging folds every queueing spike straight in |
| `halfRtt` | min trip / 2, and **survives a track change** | a `seek` frame is stale by one downlink; the lead is needed hardest in the first moment after a `current`, when there are no error samples left |
| `rateFor()` | latched: engages past ±50 ms, then `1 + 0.15 × error` clamped to `[0.98, 1.02]` until inside ±10 ms | proportional only, over a hysteresis pair rather than a dead zone. See below |
| `shouldSeek()` | 50 ms for a track's first reading, 750 ms after that | a jump at 0.3 s into a track is inaudible; fifteen seconds of arriving late is not |
| `seeked()` | drops the window after acting on it | otherwise the same stale error is applied again next reading and the correction runs away from itself — measured, it reached 10¹⁵ ms before the guard existed |
| `drift` | slope of `error + Σ(rate−1)·dt`, `null` under 60 samples or 5 minutes | read-only. See §4 |

### Why the loop latches instead of aiming at zero

Aiming at zero does not work, and the reason is not visible in any spread
statistic. The error estimate carries a few milliseconds of jitter noise however
well it is filtered, so a loop aiming at zero never arrives — it hunts across
1.0 four times a second, and **a resampling ratio that never holds still is
audible in a way that being twenty milliseconds behind the room is not.** That
was a listening report against a running room, not a measurement; the simulator
has no ears and rated the hunting version higher on spread alone.

The first answer was a dead zone with slope — `sign(e) · max(0, |e| − 25 ms)`,
easing in from 1.0 at the edge of the band. It has one threshold, and that is the
flaw: a loop that stops correcting where it starts correcting **rests on the
threshold**, which is the one place the estimate's jitter is guaranteed to keep
crossing. A browser log from a laptop against a LAN server, error parked between
19 and 28 ms with the band at 25:

```
sample: raw=16.3ms filtered=27.7ms rtt=6.0ms   rate: 1.0004 -> 1.0004
sample: raw=9.1ms  filtered=18.9ms rtt=6.0ms   rate: 1.0004 -> 1.0000
sample: raw=26.4ms filtered=26.6ms rtt=46.0ms  rate: 1.0000 -> 1.0002
sample: raw=6.4ms  filtered=6.4ms  rtt=3.0ms   rate: 1.0002 -> 1.0000
```

The corrections are tiny — under a cent — but the rate is being written
indefinitely, which is exactly what the band was built to stop. Widening a single
band does not fix it; it moves the resting threshold somewhere else.

So: **two thresholds, and a latch between them.**

| | |
| --- | --- |
| engages | past `syncDeadbandSeconds` = **50 ms** |
| steers on | the whole error, `1 + 0.15 · e` — not the part beyond the band |
| releases | inside `syncTargetSeconds` = **10 ms**, to exactly 1.0 |

Steering aims *past* the release point, at zero, deliberately. A correction that
faded out as it approached 10 ms would approach it and never cross — the same
never-arriving loop, relocated. Aiming at zero puts the release a finite ~11 s
after engaging (`ln(50/10) / 0.15`), and then the rate is 1.0 with 40 ms of room
on either side of it. Walking that 40 ms back on drift alone takes a quarter of
an hour at 50 ppm, so **that is roughly how often a settled client touches the
rate at all**.

The cost is a step at the engage point, up to 0.75 %, where the dead zone eased
in from 1.0. That is a tenth of the pitch budget, paid minutes apart instead of a
smaller one paid every few seconds — and the eased-in version's smoothness was
what put the resting point on the threshold in the first place.

What the dead-zone build bought over no band at all, measured over the scripted
session (the latch's own numbers are unmeasured — the simulator has no ears and
this change was made on a listening report and the log above):

| client | rate is exactly 1.0 | mean change between commands |
| ------ | ------------------- | ---------------------------- |
| LAN (10 ± 2 ms)   | **100 %** | 0 ppm   |
| Home (60 ± 8 ms)  | 99 %      | 7 ppm   |
| Mobile (180 ± 40) | 64 %      | 88 ppm  |
| Sat (600 ± 90)    | 53 %      | 551 ppm |

Before it, every client rewrote `playbackRate` on every sample, four times a
second, for the whole session.

`session.status` and the sync spacing both read the loop's own latch through one
`converging` getter, so the word on the strip cannot disagree with what the rate
is doing, and neither can flap: `catching up` is the latch (or a reading outside
the band that is about to trip it), `synced` is everything else.

### Why there is no integral term

A proportional controller against a device whose clock runs `d` fast settles at
`d / gain` of steady error. At a generous 120 ppm and gain 0.15 that is **0.8 ms**.
An integral term would buy less than a millisecond and cost the windup that comes
with one — which was measured too: an early PI version wound to 1176 ppm and
3316 ppm on the two jittery clients and made the spread *worse* (30 ms vs 11 ms).
The proportional loop absorbs drift by construction. It does not need to be told
about it.

---

## 3. What it measures out at

`tools/roomSim.mjs`, `SEED=1`, both modes against the same live backend. Four
headless clients, each behind its own ordered delay pipe, each with its own audio
clock error, scripted through join → add → late joiner → `skipto` → pause/resume.

**Spread between listeners** — the number that matters, since it is how far apart
two people in the room actually are:

| phase | today | with the loop |
| ----- | ----- | ------------- |
| three clients (10 / 60 / 180 ms ping) | **111 ms** p50, 112 max | **19 ms** p50, 22 p90, 29 max |
| + a 600 ms satellite joins late | 326 ms p50 | 28 ms p50, 53 p90 |
| after `skipto` | 334 ms p50 | 28 ms p50 |
| after pause and resume | 278 ms p50 | 37 ms p50 |
| whole session | 321 ms p50 | 26 ms p50, 53 p90 |

These are the dead-zone build, whose band was ±25 ms: the right-hand column sits
just inside it, which is the point — the loop stops where it was told to stop.
The latch that replaced it releases at 10 ms and engages at 50, so its resting
spread is the same order and its rate is still far more often. An earlier build
with no band at all reached 2–6 ms p50 and was rejected on listening — see §2.

**Per listener, distance from the room's median position:**

| client | ping / jitter | today p50 / p90 | with the loop p50 / p90 |
| ------ | ------------- | --------------- | ----------------------- |
| LAN    | 10 ± 2 ms   | 31 / 34 ms   | **0 / 2 ms**   |
| Home   | 60 ± 8 ms   | 0 / 30 ms    | **0 / 4 ms**   |
| Mobile | 180 ± 40 ms | 78 / 91 ms   | **3 / 10 ms**  |
| Sat    | 600 ± 90 ms | 293 / 309 ms | **17 / 46 ms** |

Read the right-hand column against the left-hand one and the shape of the result
is the whole point: **ping is compensated away completely** — Home at 60 ms is
exactly as accurate as LAN at 10 ms — and what is left is proportional to
**jitter**, not to latency. A 600 ms satellite lands inside 20 ms. A stable
60 ms link is indistinguishable from a LAN.

Hard seeks over the session: two per client, both of them a track's opening snap.
Every other correction in the run was the rate.

---

## 4. The 98–102 % budget, and whether anyone hears it

2 % is 34 cents of pitch. That is audible on a sustained tone if you are looking
for it — which is why the loop essentially never spends it:

| |
| --- |
| Rate reaches the ±2 % clamp only past **133 ms** of error (`0.02 / 0.15`) |
| Errors that large are handled by `shouldSeek`, not by the rate, in every case except a slow drift out |
| On a stable link the commanded rate is **exactly 1.0, always** — the latch means it is never written at all |
| While actually correcting, the mean step between commands is **7–551 ppm**, i.e. 0.0007–0.055 % |
| 0.055 % is **1 cent** — an order of magnitude under the ~10-cent JND, and that is for pure tones, not music |

Set `element.preservesPitch = false`. The default (`true`) time-stretches, which
is a phase-vocoder and audibly grainy on transients; with it off the browser
resamples, which at 1.3 cents is nothing. The pitch shift is the cheaper artifact
here by a wide margin.

### Clock drift is a readout, not a control input

The loop already cancels drift, so `SyncClock.drift` exists only to answer
"is this device's clock off, and by how much" — and it is honest about not
knowing. A 50 ppm device accumulates 3 ms per minute; against 5–30 ms of link
noise that trend does not rise out of the floor for several minutes, so `drift`
returns `null` under 60 samples or a 5-minute span. In a 95-second simulator run
it correctly reports `unknown` for all four clients. Show it in the session strip
once it has an answer, and never before.

---

## 5. Build order

All of it is **done and green**: `npm test` (61), `npm run type-check`,
`npm run lint`, `svelte-check` (0 errors), `npm run build`, and the simulator
gate in §9.

### ✅ 1. `src/lib/syncClock.ts`

The estimator above. Every constant is exported so the tests and the simulator
steer the same loop the browser does.

### ✅ 2. `src/lib/syncClock.test.ts`

14 cases: half-trip credit, least-queued selection, the rate clamp, the opening
snap and its one-shot-ness, the runaway guard, the `halfRtt` survival, and the
drift observer both refusing a short window and recovering a known 200 ppm device
from a five-minute one.

### ✅ 3. `tools/roomSim.mjs`

The measurement harness. Real websockets against a real backend; the only fake
parts are the network delay, the audio hardware, and the buffering time. It
imports `src/lib/syncClock.ts` directly (Node strips the types), so the thing
under test is the shipping module.

Notes that cost time to learn, and are now comments in the file:

- **The delay pipe must not reorder.** TCP does not. An independent random delay
  per frame does, which invents failures that cannot happen — a `queue` frame
  overtaking its own `current`. The pipe carries a monotonic cursor.
- **Only one `sync` may be in flight.** Replies carry no request id, so with two
  outstanding you match a reply to the wrong send time and compute a 1 ms RTT on
  a 600 ms link. Self-clock it: send the next when the last one lands, floored at
  `minSyncSpacingMs`.
- **The `loaded` guard must key on the item, not the index.** An `add` to an idle
  room re-arms the barrier on the same index 0 the empty room already handed out.
  An index-only guard sits silent and the room never starts — the simulator
  deadlocked exactly this way before the key included the track id. `session.svelte.ts`
  already gets this right via `positionedAt` + `currentTrackId`; do not regress it.
- **Seed the jitter.** A run that fails has to be a run you can run again.

### ✅ 4. `src/state/audio.svelte.ts`

One field:

```ts
rate: number = $state(1);
```

### ✅ 5. `src/state/session.svelte.ts`

Replace `correctDrift` and the `syncIntervalMs` interval.

| now | becomes |
| --- | ------- |
| `setInterval(() => this.send('sync'), 1000)` | self-clocked: a timer at `minSyncSpacingMs` that skips while a reply is outstanding, recording `sentAt` |
| `case 'sync': this.correctDrift(Number(argument))` | `clock.sample(sentAt, now, reported, audio.currentSeconds)`, then either `shouldSeek` → write `audio.currentSeconds` and `clock.seeked()`, or `audio.rate = clock.rateFor(error)` |
| `case 'seek': audio.currentSeconds = Number(argument)` | `+ clock.stalenessOf(stamp)` — the frame was measured at broadcast and is one downlink stale on arrival. See §10 |
| `driftTimer` / the `'catching up'` timeout | drop it. The status now follows the loop itself: `catching up` while the rate is latched on, `synced` otherwise |
| — | `clock.reset()` inside `setCurrent`'s `if (changed)` block, and in `connect()` |
| — | `audio.rate = 1` alongside every `clock.reset()` |

`describe('drift')` asserted the old half-second rule and is now
`describe('the shared clock')`: nine cases over the same `FakeSocket` harness,
under `vi.useFakeTimers({ toFake: [..., 'performance'] })` — the loop is timed off
`performance.now()`, so faking the timers without it makes every trip read as one
enormous round trip. Its `roundTrip` helper advances to the moment the request
actually leaves rather than by the interval, because the interval's own phase
would otherwise be added to every measured trip.

### ✅ 6. `src/components/player/layers/audio/Audio.svelte`

```ts
$effect(() => {
    if (!element) return;
    element.preservesPitch = false;   // resample, do not time-stretch
    element.playbackRate = audio.rate;
});
```

`interpolate()` already reads `element.playbackRate`, so the position reporting
follows for free — this is why the AudioContext clock work in
`playbackClock.ts` was worth doing first.

The existing seek `$effect` uses a 0.25 s tolerance, comfortably above anything
the rate loop does, so it will not fight the correction. Leave it.

### ✅ 7. Session strip

`session.status` gains no new words. Add two quiet readouts next to it:

Everything the loop knows, in brackets after the state word, in the order the
loop works in — how far out we are, what that was measured over, what is being
done about it, and what the device does on its own:

```
synced [+3 ms · 58 ms rtt · 1.0004× · ? ppm]
```

`?` is the drift before it has an answer, which is minutes. The `title` spells
all four out in words, since four bare numbers in a strip are a puzzle.

`status` gained no new words but lost its timeout: `catching up` and `synced` now
follow the loop's latch, which engages at 50 ms and lets go at 10.

The readouts are **mirrored** onto `$state` fields on `Session` — `offsetMs`,
`pingMs`, `driftPpm` — rather than exposed as getters over the clock. `SyncClock`
is a plain class, so nothing about mutating it is reactive, and a getter over it
leaves the strip showing whatever it first rendered. `rewind()` clears the offset
and keeps the other two: the link and the device did not change.

Still to do: the output offset knob, specified in §7 and deliberately unbuilt.

---

## 6. What no amount of protocol can fix

The loop holds a client on the **position the browser reports**. Between that and
the listener's ear sit two things it cannot see:

**Output latency the browser will not admit to.** `AudioContext.outputLatency`
is already subtracted in `playbackClock.ts`, and Firefox reports `0` for it.
Two browsers with different hardware buffers are offset by the difference and
nothing in this plan can measure it.

**Bluetooth.** A2DP adds 100–200 ms, sometimes more, and no browser API reports
it. A listener on AirPods is a fifth of a second behind everyone else however
perfect the clock sync is — which is more than the entire error this plan
removes.

Neither is measurable from inside the page, so neither can be corrected
automatically. The answer is a number the listener sets by ear — specified in §7,
**not built**. Ten milliseconds of protocol accuracy handed to a Bluetooth headset
is ten milliseconds of accuracy thrown away.

Other limits worth stating plainly rather than debugging later:

- The accuracy floor is **path asymmetry**, not ping. A link whose jitter is
  ±90 ms cannot be held to 10 ms by anything that measures over it.
- `stop` does not stop the server's clock (§3.8 of the API doc). After a `stop`
  the error is however long the room sat stopped, and the hard-seek path is what
  catches it. That is correct, and it will be an audible jump.
- `playpause` resuming broadcasts `playing True` with no `seek`, so the resume
  transient is the loop's to clean up — measured at 22 ms p50, settling within a
  few seconds.
- A background tab throttles rAF but not `timeupdate`; the position stays live,
  the interpolation goes coarse. Already handled in `Audio.svelte`.

---

## 7. The output offset knob — specified, not built

The one correction the loop cannot make for itself, written down so the decision
to build it can be made on facts rather than remembered from a conversation.

### What it is

One signed number per device, in milliseconds: **how much later the sound
actually leaves this device than the browser claims it does.** Default `0`.
Bluetooth is the case that matters, and it is positive: 100–200 ms for A2DP,
more for some codecs and some phones.

It is not a room setting, not a per-track setting, and never travels to the
server. Two people on the same account and different headphones need different
values, which is the whole reason it is stored per browser rather than per user.

### Where it enters

One place, and it must be exactly one place. `audio.currentSeconds` is already
the *audible* position — `playbackClock.ts` subtracts `AudioContext.outputLatency`
from the context clock for precisely this reason. The knob is what
`outputLatency` failed to report, so it belongs on the same side of the
subtraction:

```ts
// src/lib/syncClock.ts
/** What `AudioContext.outputLatency` did not admit to, in seconds. Positive
 *  means this device's sound comes out later than it says it does. */
outputOffset = 0;

sample(sentAt, receivedAt, reported, position) {
    …
    this.samples.push({ err: reported + rtt / 2 + this.outputOffset - position, rtt });
```

and the same term on the seek lead, since a `seek` frame is placing the audible
position too:

```ts
get lead() { return this.halfRtt + this.outputOffset; }   // replaces halfRtt at the `seek` call site
```

`session.svelte.ts` sets `this.clock.outputOffset` in `connect()` and whenever
the stored value changes. Nothing else reads it.

**Sign check, because getting it backwards is silent and awful:** a positive
offset makes `error` more positive, which speeds the player up and moves it
*ahead* in the stream, so that sound emerging late lands on time. A listener on
AirPods enters `+150`.

### Where it is stored

`musicrain.output-offset`, beside `musicrain.username`, in the guarded shape
`src/state/user.svelte.ts` already uses — `browser` check, `localStorage`,
`Number.parseInt` with a `0` fallback for anything unparseable. It does **not**
belong in `user.svelte.ts` itself: identity is per person, this is per device,
and the Discord path adopts a name without adopting a headset.

### Where it lives in the UI

The profile panel behind the header's avatar button, under the name field — the
panel `MULTIPLAYER_PLAN.md` Step 2 already hangs off that control. Not in the
session strip: the strip is per-room and this is not.

- `<input type="range">` from −500 to +500, step 5, with the live number beside
  it, and a number input for anyone who knows their headset's figure.
- Copy has to say what it is for, because nobody goes looking for a control they
  do not know they need: *"Bluetooth headphones play late. If you are behind
  everyone else in a room, add the delay here."*
- It must be reachable **while a room is playing**, since that is the only
  moment it can be set. See below.

### How anybody is supposed to set it

Honestly: by ear, and only by ear. There is no browser API for this, and the
strip's `offsetMs` readout cannot help — the error the knob corrects is by
definition the part the browser cannot see, so the readout says `0 ms` while the
listener is a fifth of a second behind the room.

The procedure is two devices in one room playing the same track, nudging until
the flam collapses. That is why the control has to be adjustable during
playback and why the step is 5 ms rather than 1: below about 10 ms nobody can
hear which direction to go, which is also the point at which the knob has done
its job.

### What it must not do

- **No guessing from `navigator.userAgent` or device labels.** A wrong automatic
  value is worse than no value, because the loop will faithfully hold the client
  at the wrong position and the readout will report success.
- **No syncing it to the room.** It describes hardware, not a session.
- **No applying it to local playback.** Outside a room it corrects nothing and
  only shifts the seek bar against the sound.

### What it costs, and the check it needs

Roughly: two lines in `syncClock.ts`, one call site moved from `halfRtt` to
`lead`, one assignment in `session.svelte.ts`, a small store file, and the panel
control. The runnable check is one case in `syncClock.test.ts` — a device with a
`+150 ms` offset reports an error 150 ms larger than the same device without one
— plus one in the session tests that a `seek` frame lands the lead *and* the
offset. `tools/roomSim.mjs` cannot test it: it has no ears and no output stage,
which is the same reason the browser cannot.

### Why it is not built

It is a UI decision — where it lives, how it is worded, whether a listener who
does not understand the problem is more likely to be helped or confused by a
control they can set wrongly. The clock work does not depend on it, and the
sign convention above is the only part that is hard to get right later.

---

## 8. Backend asks

One was a bug and is now fixed. The other three are improvements the sync loop
does not need — every measurement above is from the backend as it stands.

### ✅ `seek` past 15 minutes overflowed the clock

`VirtualPlayer.TimeSpanToTimestamp` multiplies before it divides:

```csharp
return timeSpan.Ticks * Stopwatch.Frequency / 10000000;
```

On Linux `Stopwatch.Frequency` is `1_000_000_000`, so the intermediate is
`seconds × 10¹⁶` and it overflows `long` at **922.3 seconds**. Measured against
the running server — seek to a position, read it back with `sync`:

| asked | `seek` broadcast | `sync` reply |
| ----- | ---------------- | ------------ |
| 900 s   | 900.000  | 900.300  |
| 921 s   | 921.000  | 921.301  |
| **923 s** | **−921.674** | **−921.374** |
| 1200 s  | −644.674 | −644.374 |
| 3600 s  | −89.349  | −89.048  |

Three of 200 random tracks in the library are longer than that — it is a library
with mixes in it, so this is an ordinary track, not an edge case.

Three call sites are affected, and the third is the unpleasant one:

- `SeekTo` — any seek past 15:22 puts the room's clock somewhere absurd.
- `TogglePlaying` — resuming from a pause taken past 15:22 does the same.
- `GetCurrentTime` — while paused it *rebases* `StartTime` from `PauseTime` on
  every call, so a room merely sitting paused past 15:22 corrupts its own clock
  the moment anybody sends `sync`. It is a write on a read path, and after this
  plan every client sends `sync` up to four times a second.

Fixed in `VirtualPlayer.TimeSpanToTimestamp`; `double` carries it exactly, since
1e9 × 3600 is 3.6e12 and well inside a 53-bit mantissa:

```csharp
return (long) (timeSpan.TotalSeconds * Stopwatch.Frequency);
```

Covered by `VirtualPlayerTests.SeekingPastAQuarterOfAnHourStaysOnTheClock`, which
seeks to 1800 s and pauses and resumes there — and which fails on the old line.
The test pins a position rather than the 922 s boundary itself, because the
boundary moves with `Stopwatch.Frequency`: Windows QPC frequencies are lower,
which would have hidden this bug entirely. Re-probed end to end afterwards, every
position from 10 s to 3600 s now reads back where it was put.

### Improvements, none of them blocking

1. **A request id on `sync`.** `sync <token>` → `sync <token> <seconds>` would let
   more than one round trip be in flight, which is the difference between one
   sample per RTT and four per second on a slow link — the sample rate is the
   only lever left on jitter. It must be a new verb or a negotiated one: today's
   clients do `Number(arg)`, and `Number("abc 12.3")` is `NaN`.
2. ~~**Server timestamps in the reply.**~~ Done — see §10. `sync <pos> <utcMs>` is
   the three-timestamp NTP form, which is enough to put the server's own handling
   time on the uplink where it belongs instead of splitting it across both halves.
   The fourth timestamp would only refine the delay estimate, which nothing reads.
3. ~~**`seek` carrying the position it will be at broadcast, not at handling.**~~
   Superseded by §10: the frame now carries the moment it was measured, so the
   client subtracts the flight it actually took rather than approximating it.

---

## 9. Verification

Automated:

- `npm test` — `syncClock.test.ts` is the unit floor: 14 cases, and the runaway
  and one-shot-snap cases are the two that actually catch regressions.
- `node tools/roomSim.mjs` against a local `Gaida.API`. Gate: three-client phase
  **p50 ≤ 25 ms and p90 ≤ 30 ms**, every client's commanded rate inside
  `[0.98, 1.02]` throughout, and a stable link reporting **`still 100%`** — a
  well-connected client that is rewriting `playbackRate` at all is the
  regression. Last run: 19 ms p50, 22 ms p90, 29 ms max, LAN still 100 %.
  `MODE=naive` reproduces the before column.
- `npm run type-check`, `npm run lint`, `svelte-check`, build.

By hand, which the simulator cannot do because it has no ears:

- Two browsers, one throttled to Slow 3G in devtools, same room, same track,
  speakers on both. The flam should be gone. It is the only test for "is the
  rate change audible", and the answer wanted is "what rate change".
- One of them on Bluetooth: confirm the gap is there, confirm the manual offset
  closes it, and confirm that is the reason the knob exists.
- Watch `element.playbackRate` in the console across a track change and a
  `skipto`. It should sit within a few parts in ten thousand of 1.0 and return
  there within seconds of any disturbance. If it parks at a clamp, the error
  estimate is wrong, not the loop.

---

## 10. Timestamped frames, and a deadband that follows the link

A follow-up proposal, in the shape it arrived: stop steering continuously, poll the
server for its UTC clock, average the offset over one to two minutes, and correct the
audio only past ±200 ms — with the server stamping every action so the client can
place it. Followed by the constraint that actually decides the tuning: **accuracy is
not the goal, an inaudible rate is** — call it three or four rate changes per song.

That constraint is the useful one, because §2 and §3 had been optimising spread and
treating the artifact as a side condition. Turned around, most of the proposal is
right, and the part that looked most wrong turns out to be right about one client.

### The wire: every clock-moving frame carries when it was measured

`seek <seconds> <serverUtcMs>` and `sync <seconds> <serverUtcMs>`, from
`VirtualPlayer.Stamp()`. The client derives NTP's offset from three timestamps —
`skew = serverUtcMs − (t1 + t4) / 2`, held against `performance.now()` because the
wall clock steps when the OS corrects it — and places each stamped frame by the
flight that frame actually took.

This replaces `+ clock.halfRtt` at the `seek` call site. `halfRtt` is a **minimum**
over the window: the best trip the link has managed lately, and a broadcast is under
no obligation to be that trip. A `seek` that spent 300 ms queued was placed 60 ms
back, leaving that client a quarter of a second ahead of the room at exactly the
moment — a track start, a resume — when everybody is listening for it.

**`playpause` now broadcasts the position on both edges.** It always did when
pausing; resuming sent `playing True` alone and left every client to rediscover the
position from its next `sync`. §6 listed that as "the resume transient is the loop's
to clean up" — it is now the server's to state, and the position leads the state
change so a client lands on it before it starts moving. That phase measured 94 ms p50
against the unstamped server and 19–30 ms with it.

### The band: 25 ms was never one number

The proposal's ±200 ms looked indefensible — two clients inside it can be 400 ms
apart, which is a flam rather than a room. Measuring the artifact directly is what
showed the real shape of it:

| client | ping / jitter | rate changes per minute at a fixed 25 ms band |
| ------ | ------------- | --------------------------------------------- |
| LAN    | 10 ± 2 ms   | 0.0  |
| Home   | 60 ± 8 ms   | 7.3  |
| Mobile | 180 ± 40 ms | 3.1  |
| Sat    | 600 ± 90 ms | **29.4** |

The error estimate is only ever as steady as the trips it is drawn from. On a link
whose round trip wanders by 180 ms the estimate wanders too, and a 25 ms band is
crossed by noise alone several times a second — that is the whole of Sat's 29.4, and
none of it is a real correction. Widening the band globally to 60 ms silenced the
three good clients (0.0/min each) and still left Sat at 26.9, while costing LAN sixty
milliseconds of accuracy it was never going to lose.

So the band is per-link: the typical excess of a round trip over the quickest one
seen, floored at `syncDeadbandSeconds` and capped well under `hardSeekSeconds`. The
floor was 25 ms when this was measured and is 50 ms now — a fixed band and a latched
one want different floors, and the reason is in §2.

```ts
get deadband() {
    if (this.links.length < 4) return syncDeadbandSeconds;
    const trips = this.links.map(link => link.rtt).sort((a, b) => a - b);
    return clamp(trips[trips.length >> 1] - trips[0], syncDeadbandSeconds, maxDeadbandSeconds);
}
```

LAN and Home keep the floor and stay accurate to a millisecond; Sat opens to ~180 ms
and goes quiet. **The proposal's 200 ms was a good number for the client it was
imagined for, and a bad one for everyone else.** Accuracy went *up*, not down — a
satellite that sits still at 1.0 is closer to the room than one hunting across it.

### The other artifact: steering on an unfiltered window

`error` is a minimum over the window, and a minimum over one sample is that sample —
raw jitter, nothing filtered. A track change empties the window, so the readings right
after one are exactly the unfiltered ones, and steering on them cost a burst of a
dozen-odd rate changes at the top of every track. `rateFor` now holds at exactly 1
until there are `minSteeringSamples` to filter; the opening snap has already put the
client roughly in place, so there is nothing to chase meanwhile.

### Polling

250 ms while converging, 2 s once the track has settled and the loop is not latched on.
The first version of this used **half** the band, because a client hovering on a single
threshold flaps between the two spacings and gets the slow one exactly while it is
trying to converge — measured, that alone cost a 180 ms link the difference between 0
and 14 ms off the room. The latch made the halving unnecessary: it is hysteretic
already, so spacing and status now read it directly and cannot flap.

Four readings a second is what converging costs, not what holding costs. A settled
client is only watching its own clock drift, which moves about 3 ms a minute at
50 ppm, and every real disturbance now arrives as a stamped frame that places itself
without waiting for a poll.

### The window, and what a longer connection buys

The link window is 90 seconds — the proposal's "one to two minutes", implemented as a
**minimum** over that span rather than a mean. §2 has why: a round trip can only be
inflated, never shortened, so the quickest one seen is where the symmetry assumption
is least wrong, and averaging folds every queueing spike straight in.

A minimum can only improve as samples arrive, so the ping estimate does sharpen with
connection age — over roughly one window, then it plateaus, because the window slides.
The **spread** does not keep falling with age: its floor is per-sample jitter, not how
long you have been connected, which is the same result §3 reports as "what is left is
proportional to jitter, not to latency". What a long connection does buy is a
well-sized band, since the dispersion estimate feeding `deadband` comes from the same
window — a link that degrades widens its band within a window and goes quiet rather
than starting to hunt.

For the window to be worth having, samples had to stop expiring with the polling
interval: each records the correction standing when it was taken, and `error` nets out
whatever has been spent since. Without that, a sample from thirty seconds ago describes
a position the loop has already steered away from.

### What did not change, and cannot

The estimator. Working the algebra through, `reported + rtt/2 − position` and the
stamped form are the *same number* for a single sample — the stamps buy correct
placement of broadcast frames and correct attribution of server handling time, not a
better offset.

And the floor is still path asymmetry, not ping. A link that is always 180 ms up and
20 ms down biases `skew` by half the difference, and a stamped frame then reads back
as exactly `halfRtt` again. Three timestamps cannot separate a clock offset from a
lopsided path; that is NTP's limit, not an implementation gap, and no amount of
connection time averages it out.

### What it measures out at

Two runs against a live stamped backend, four clients, the §3 script:

| | run 1 | run 2 | before this section |
| --- | --- | --- | --- |
| three clients, steady | 9 ms p50, 10 p90 | 12 ms p50, 15 p90 | 12 ms p50 |
| after `skipto` | 16 ms p50 | 59 ms p50 | 38 ms p50 |
| after pause and resume | 19 ms p50 | 30 ms p50 | **94 ms p50** |
| whole session | 15 ms p50, 20 p90 | 16 ms p50, 60 p90 | 34 ms p50 |
| clients at 0.0 rate changes/min | 3 of 4 | 3 of 4 | — |
| hard seeks | 0/0/2/2 | 0/0/2/2 | 0/0/2/2 |

The two hard seeks per jittery client are both track openings, which is the design.

**Rate churn by phase** is the number the constraint asked for, and it is the reason
the per-session figure above is misleading in the right direction:

| phase | LAN | Home | Mobile | Sat |
| ----- | --- | ---- | ------ | --- |
| steady — what a song looks like | 0.0 | 0.0 | 0.0 | — |
| a listener joining | 0.0 | 0.0 | 0.0 | 0.0 |
| after `skipto` | 0.0 | 19.2 | 0.0 | 0.0 |
| after pause and resume | 0.0 | 0.0 | 0.0 | 0.0 |

**During steady playback the rate does not move at all.** What churn there is sits in
the seconds after a disturbance, in one client per run, and is the loop converging onto
a new track — which is what it is for. A typical run leaves three of four clients at
exactly 1.0 for the entire session.

Run-to-run variance is real and worth knowing: `SEED` fixes the simulated jitter, not
the real network under it, so which client catches a bad patch moves between runs. The
steady phase is the stable part, and it is stable at zero.

### Verification

`npm test` (80), `npm run type-check`, `npm run lint`, `svelte-check` (0 errors), the
build, and `dotnet test` (32) are green. The wire change was confirmed against a live
`Gaida.API` — stamps arrive 0–1 ms old on loopback, pause and resume round-trip the
position exactly.

The §9 gate now passes: three-client p50 9–12 ms (≤ 25), p90 10–15 ms (≤ 30), every
commanded rate inside `[0.98, 1.02]`, LAN still 100 %. It did **not** pass on the first
stamped build — 32 ms p90 — which is what the deadband and steering work above fixed.
`MODE=naive` still reproduces the before column, and the client still drives an
unstamped server: the fallback to `halfRtt` held the room at 12 ms p50 through the
steady phase.
