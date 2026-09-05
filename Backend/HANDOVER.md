# Gaida — Handover Notes

**Date:** 2026-08-31 · **Branch:** `feat/discord-bot` · **Scope:** repo-wide over-engineering audit, `Result<TOk,TError>` removal, streaming migration.

Everything below was verified on this machine against the live library at `/nvme0/DiscordBot/Music Database` (5,934 indexed entries, 645 artist folders). Where a claim is unverified it says so.

---

## 1. State at handover

| Check | Result |
|---|---|
| `dotnet build Gaida.slnx` | clean, 0 warnings (excluding the DSharpPlus submodule) |
| `dotnet test Gaida/Gaida.Tests` | 3 / 3 pass (incl. the live YouTube download test) |
| `/Audio/Search` (ID + keywords), `/Audio/RandomResults`, `/Audio/Artist/*` | verified |
| The same four, streamed element by element (`STREAMING_PLAN.md`) | built and unit-tested; not yet measured against a live deployment |
| `/Audio/DownloadRaw`, `/Audio/Download/{codec}/{bitrate}` | verified (9.5 MB raw, 3.3 MB opus) |
| Multiplayer websockets (create → push → join → add → rename → loaded → sync) | verified, 0 server-side errors |
| `/openapi/v1.json` | 200 |
| Library `Info.json` round-trip (System.Text.Json) | verified, no rewrites lost data |

Net change: **−1,146 lines, −9 dependencies.**

---

## 2. The WavPack finding

### 2.1 Correction — my first diagnosis was wrong

I initially reported this as *"ffmpeg can't decode WavPack through a pipe."* **That is incorrect.** A real WavPack track transcodes fine end-to-end:

```
audio://qustone--Zm  (Queen - Stone Cold Crazy.wv)
  DownloadRaw            → HTTP 200, 7,349,928 B
  Download/Opus/96       → HTTP 200, 1,564,513 B   ✅
```

### 2.2 What is actually wrong

The failing ID pointed at a **`.wvc` file — a WavPack *correction* file**, not an audio stream. WavPack hybrid mode emits a lossy `.wv` plus a `.wvc` correction companion; `.wvc` is not independently decodable, and ffmpeg reports `[wavpack] Packed samples not found` on every frame.

Three separate entries exist in `Rock/Queen/Info.json` for one song:

| ID | RelativeLocation | On disk | Result |
|---|---|---|---|
| `qustone--up` | `Queen - Stone Cold Crazy.flac` | **missing** | HTTP 500 |
| `qustone--Zm` | `Queen - Stone Cold Crazy.wv` | yes | works |
| `qustone--md` | `Queen - Stone Cold Crazy.wvc` | yes | HTTP 200, **0 bytes** |

Reproduced verbatim:

```
audio://goone-ni-bZ  (missing .flac) → raw: HTTP 500 | opus: HTTP 500
audio://qustone--md  (.wvc)          → raw: HTTP 200 9,509,694 B | opus: HTTP 200 0 B
audio://qustone--Zm  (.wv)           → raw: HTTP 200 7,349,928 B | opus: HTTP 200 1,564,513 B
```

### 2.3 Scale — this is a library-index problem, not a codec problem

Full scan of all 645 `Info.json` files:

| | count |
|---|---|
| Total indexed entries | **5,934** |
| Entries pointing at a **non-audio** file (`.wvc` ×1,778, `.mkv` ×4, `.vtt` ×1) | **1,783** |
| Entries whose file **no longer exists** | **1,793** |
| Titles indexed more than once | **1,782** |
| Entries that resolve to a playable, existing file | **≈2,358 (40 %)** |
| `Info.json` files that are 0 bytes | 26 |

**About 60 % of the search index is unplayable.** Random results, keyword search, and multiplayer queues all serve these IDs today.

### 2.4 Why the entries persist

`MusicManager.ParseArtistFolder` never prunes:

- if `existing.Count == songs.Count` it returns the stored list untouched;
- otherwise it appends files not already referenced by `RelativeLocation`.

Neither path removes an entry, so a track that was re-encoded (`.flac` → `.wv` + `.wvc`) leaves its stale entry behind forever. The current extension filter *rejects* `.wvc` (`"x.wvc".EndsWith(".wv")` is `false`), and `git log -S` shows only one commit ever touched that filter — so the current scanner cannot create these entries. They predate this code (the database was backported from an older project) and nothing has removed them since.

### 2.5 Suggested fix (not applied — it rewrites user data)

Prune on load instead of counting. Roughly:

```csharp
// drop entries whose file is gone or isn't audio, then add the new ones
existing.RemoveAll(m => m.RelativeLocation is null
                        || !IsAudioBasedOnFileExtension(m.RelativeLocation)
                        || !File.Exists(Path.Combine(StorageDirectory, m.RelativeLocation)));

var changed = existing.Count != before || newFiles.Count > 0;
```

Two consequences to decide on before running it:

1. **IDs of removed entries die.** `audio://` links shared previously (Discord, multiplayer rooms) would 404. Pruning ~3.5k entries is a one-way door — consider logging the removals to a file first.
2. **Duplicates remain** even after pruning (a `.flac` and a `.wv` of the same track both exist in some folders). If one canonical entry per track is wanted, dedupe by `RomanizedAuthor + OriginalTitle`, preferring the lossless/larger file.

Also worth fixing at the same time: `Content.DownloadRaw` / `Download` return **500** when no downloader can serve the result. A missing file is a **404**.

---

## 3. Bugs found and fixed during the refactor

These were all pre-existing; each was a one-line-ish fix inside code the audit was already rewriting.

| # | Where | Was | Now |
|---|---|---|---|
| 1 | `Utils/Performance.SliceAfter` | returned the text **before** the needle (identical to `SliceTo`) — YouTube playlist ID extraction never worked | returns the text after it |
| 2 | `AudioManager.FindQueryType` | built `"yt://://"` from an already-complete identifier, so `QueryType.ID` was never returned | splits once, matches the real identifier |
| 3 | `Content.Search` | stripped the `yt://` protocol *before* calling `SearchID`, which needs it to pick a platform | passes the query through |
| 4 | `AudioManager.HandleStreamSpreaders` | `if (expire < now) continue;` — disposed **live** streams and kept expired ones | `>` |
| 5 | `ManagerService.ExpireTimer` | constructed and wired but never started; ffmpeg encoders never expired | `Enabled = true` |
| 6 | `Multiplayer.Rooms` / `.Join` | returned `Ok()` after the websocket closed → `InvalidOperationException` on every disconnect | `EmptyResult` |
| 7 | `Content.StreamToResponse` | an aborted client left the request waiting on a semaphore that only the source could release | waits on `HttpContext.RequestAborted` |
| 8 | `.gitignore` | only ignored `.env`, so **`Gaida.Bot/.env.json` (Discord tokens) was untracked-but-not-ignored** | `.env.json`, `*.user`, `Gaida.API/cache/` ignored |
| 9 | `MusicManager.SearchById` | `id[..^2]` threw on IDs shorter than 2 chars | length-guarded |
| 10 | `Content.Download` | unknown codec sent an `audio/mp3` header over an opus/mka body | one tuple switch, `audio/mka` + opus default |

---

## 4. Known issues NOT fixed

Ranked by likely impact.

1. **~60 % of the music index is unplayable** — see §2. The single biggest correctness issue in the repo.
2. **`StreamSpreader` holds the whole stream in RAM.** Every chunk stays in `Data` until dispose, so a cached 10 MB track costs 10 MB per distinct ID for 45 minutes (`AudioManager.ExpireTimeSpan`), and subscribers that join late replay from index 0. The file's own comment (`// allocation king, gc go brrrr`) is accurate. Fine at current scale; it is the first thing that breaks under concurrency.
3. **A dead subscriber is only removed on the *next* sync pass.** `SyncSubscribers` enqueues the removal and continues; nothing else evicts it if no more data arrives. The HTTP path no longer hangs on this (fix #7), but the subscriber object leaks until the spreader expires.
4. **`FFmpegEncoder.Convert` passes `-d copy`.** Meaningless (verified harmless — ffmpeg ignores it and output is byte-identical without it). Delete it when someone next touches that string.
5. **Spotify never produces audio, and its pod is unofficial.** A `spotify://` result is a name; Gaida.API resolves each one against the library and then YouTube (`PlayableResolver`), so a `spotify://` ID never reaches a client. The pod is Python over [SpotAPI](https://github.com/Aran404/SpotAPI) rather than the official API, which is why it needs no credentials at all — and why it is the pod most likely to start finding nothing after a change at Spotify's end. Every route degrades to "found nothing", so a break is a quiet loss of Spotify results, not an outage. Its egress IP is this host's.
6. **26 empty `Info.json` files** sit at genre level, created by `File.Open(..., OpenOrCreate)`. Harmless, but they are why a plain `json.load` fails on the tree.
7. **`Info.json` files carry a UTF-8 BOM** in ~170 folders. `System.Text.Json` skips it transparently (verified: 0 "Malformed Info.json" across 8 full library loads), but any external tooling must open them as `utf-8-sig`.
8. **`.idea/.idea.AudioAPI/`** is stale IDE state from before the project rename. Untracked and ignored; delete when convenient.
9. **`Gaida.API/cache/YouTube.json`** was build output living in the source tree. Now ignored, not deleted.

---

## 5. Conventions after the `Result` removal

The `Result` project is gone. `Status`, `Empty`, `InvalidResultAccessException`, `SearchError`, `DownloadError`, `FFmpegError`, `WebSocketReadStatus` are all deleted.

| Concept | Signature |
|---|---|
| Lookup that may miss | `Task<T?>` — `null` means not found |
| Search / listing | `IAsyncEnumerable<PlatformResult>` — empty means nothing found |
| Content fetch | `Task<StreamSpreader?>` |

**Search streams end to end.** `AudioManager` → `Platform` → `SearchProvider` are all `IAsyncEnumerable`, and the MVC actions return the enumerable directly, so results reach the client as each provider yields them rather than after every platform has finished.

Two helpers in `Gaida.Core/Utils/Streaming.cs`:

- `.Guarded(logger, context, ct)` — enumerates a source that may throw, logs and **ends that sequence** instead of propagating, so one failing provider falls through to the next. Providers therefore no longer need their own try/catch.
- `.AsAsync()` — adapts an in-memory sequence (the local music DB) to the streaming interfaces.

**Adding a platform** (`IPlatformFactory<T>` is gone):

```csharp
manager.RegisterPlatform(new YouTube(logger));   // calls platform.Initialize() once
```

`Platform.Initialize()` sorts providers by `Priority` and wires the content getters — it is called exactly once, by `RegisterPlatform`. There is no separate `AudioManager.Initialize()` any more (it used to double-initialize every platform, reloading the whole music database twice).

**Serialization:** `OriginalTitle` / `OriginalArtist` moved onto `PlatformResult`, so derived types add no serialized members and the framework serializes the enumerable directly. `SerializeSelf`, its `MusicResult` override, and the hand-built `'[' + string.Join(',') + ']'` in `CustomSerializer.ToJson` are gone.

`MusicInfo` now uses System.Text.Json with **no `[JsonPropertyName]` attributes** — those attributes were dead under Newtonsoft, so the on-disk names are the .NET property names. Verified round-trip against the live library; do not add naming attributes back without migrating the files.

---

## 6. Dependency changes

**Removed:** `Microsoft.EntityFrameworkCore.Design` / `.Sqlite` / `.SqlServer` and a `<Reference>` with a hardcoded `..\..\..\.nuget\packages\...\9.0.0\` HintPath (never used, and broken on any other machine) · `VideoLibrary` · `YoutubeSearchApi.Net` · `SpotifyAPI.Web.Auth` · `Swashbuckle.AspNetCore` · `Serilog.Extensions.Logging` (already in `Serilog.AspNetCore`) · `JetBrains.Annotations` · `Newtonsoft.Json` usage · the local `Result` project.

**Added:** `Microsoft.AspNetCore.OpenApi` 10.0.11, replacing Swashbuckle (also clears the `NU1903` advisory on `Microsoft.OpenApi` 2.4.1).

**Moved:** every platform dependency out of `Gaida.Core` — which used none of them — into the platform project that does. `Gaida.Core` now references **Serilog only**.

YouTube kept two backends (YoutubeExplode + yt-dlp) of the four it had; `GetterVideoLibrary` and `YouTubeSearchProviderMadeyoga` were deleted.

---

## 7. First things I would do next

1. Decide on the pruning policy in §2.5 and run it — it fixes the largest user-visible problem.
2. Change "no downloader could serve this" from 500 to 404.
3. Cap or window `StreamSpreader.Data` before the service sees real concurrency.
