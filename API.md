# Public Audio API

The example production base URL is `https://api.example.com`. All endpoints below are rooted at `/Audio` and use JSON unless they stream audio.

## Discovery result

`GET /Audio/Search?query={term}`, `GET /Audio/RandomResults?count={count}`, `GET /Audio/Artist/Local?term={artist}`, and `GET /Audio/Artist/YouTube?term={artist}` all return an array with exactly this shape:

```json
{
  "id": "audio://example-id",
  "name": "Track title",
  "artist": "Artist name",
  "album": "Album title",
  "contentUrl": "https://api.example.com/Audio/DownloadRaw?id=audio%3A%2F%2Fexample-id",
  "duration": "00:03:45",
  "thumbnailUrl": null,
  "originalTitle": "Заглавие",
  "originalArtist": "Изпълнител"
}
```

`id`, `name`, `artist`, `contentUrl`, and `duration` are always non-null. Local-library IDs always begin with `audio://`; YouTube IDs use `yt://`. `album`, `thumbnailUrl`, `originalTitle` and `originalArtist` may be `null`. `contentUrl` is an absolute downloadable raw-audio URL.

`originalTitle` and `originalArtist` carry the untransliterated title and artist as the source tagged them. **`name` and `artist` are sourced from them when present**, falling back to the romanized form, so a track tagged in Cyrillic or Japanese renders in its own script by default and `name` will usually equal `originalTitle`. The romanized form is not on the wire — it is used server-side for search and matching only. Clients that want the romanized string should not expect it here.

- `Search` accepts normal text (including Cyrillic), local IDs, the server's `yt://` IDs, and playlist URLs.
- `RandomResults` accepts `count` from 1 through 200, inclusive. An invalid count returns `400 {"error":{"code":"invalid_count","message":"..."}}`.
- `RandomResults` also accepts `youTubeShare` from 0 through 1 (default `0.4`): the share of results drawn from YouTube, with the local library supplying the rest and backfilling anything YouTube is short of. Out of range returns `400 {"error":{"code":"invalid_share","message":"..."}}`.
- Artist endpoints return `200 []` when the artist is empty or unmatched. Both use `artist`, then `name`, then `id` as their stable sort order.

## Mixed query resolver

`GET /Audio/FindQueryType?query={value}` resolves pasted values. The response has a machine-readable `kind` discriminator.

| Input | `kind` | Response fields |
| --- | --- | --- |
| `audio://...` | `local` | `query` is the canonical local ID; `result` is one discovery result |
| YouTube video URL, `yt://...`, or 11-character video ID | `youtubeVideo` | `query` is canonical `yt://...`; `result` is one discovery result |
| YouTube playlist URL, `yt-playlist://...`, or recognised playlist ID | `youtubePlaylist` | `query` is a canonical playlist URL; `playlistId`; `results` discovery-result array |
| Ordinary text | `search` | `query` is the trimmed text; call `Search` with it |

Examples:

```json
{"kind":"youtubeVideo","query":"yt://dQw4w9WgXcQ","result":{"id":"yt://dQw4w9WgXcQ","name":"...","artist":"...","contentUrl":"...","duration":"00:03:33","thumbnailUrl":"..."}}
```

```json
{"kind":"youtubePlaylist","query":"https://www.youtube.com/playlist?list=PL...","playlistId":"PL...","results":[]}
```

Malformed `audio://`, `yt://`, `yt-playlist://`, or unsupported URLs return a JSON error such as:

```json
{"error":{"code":"invalid_query","message":"The YouTube video ID is invalid."}}
```

Known but missing local/video IDs return `404` with `code: "not_found"`. A temporary resolver failure returns `503` with `code: "resolver_unavailable"`; no resolver error is sent as HTML or an empty body.

## Local variants

`GET /Audio/Local/Variant?name={video title}&artist={channel title}&duration=00:04:32` asks whether the local library already has the recording a YouTube result names. It searches the in-memory library only — no YouTube call, no cache lookup — so it is cheap enough to run after every roll. `204` when nothing is worth offering, `400 invalid_query` for a missing `name` or an unparseable `duration`.

```json
{
  "match": "same",
  "score": 0.97,
  "durationDeltaSeconds": 11,
  "youTubeTags": [],
  "libraryTags": [],
  "result": {"id": "audio://ramsonne-x9", "name": "Sonne", "artist": "Rammstein", "duration": "00:04:32"}
}
```

`result` is an ordinary discovery result. `match` is `same` (the tag sets agree), `variant` (a tagged upload answered with the plain library copy) or `weak` (offer it, but say so). Renditions run one way only: an `(Instrumental)` upload may be answered with your plain copy, a plain upload is never answered with your instrumental, and `(Remastered)` counts as the same performance. `durationDeltaSeconds` is library minus upload and is reported, never a reason to reject a `same` or `variant` — uploads carry intros.

## Artwork

`GET /Audio/Cover?id={id}` returns the track's artwork as an image, for any local or YouTube ID. The API fetches the upstream thumbnail once and keeps it on disk, so repeat requests never touch the platform layer; the first request streams to the caller while it downloads rather than waiting for the whole image. An ID with no artwork returns `404 {"error":{"code":"not_found","message":"..."}}`, a missing ID `400 invalid_query`.

Responses are `Cache-Control: public, max-age=604800`. The cache directory comes from the `THUMBNAIL_CACHE` environment variable and defaults to `gaida-thumbnails` under the system temp directory.

This is what an embedded Discord activity uses for every thumbnail: the discovery endpoints hand out absolute ytimg and cover-host URLs, and the activity iframe can only reach hosts it has a URL mapping for.

## Library tree

`GET /Audio/Browse?path={folder}` returns one level of the local library's folder tree, so a client
can open folders one at a time instead of pulling the whole thing. Omit `path`, or send an empty
one, for the root.

```json
{
  "path": "Bulgarian/Естрада",
  "folders": [
    { "name": "Братя Аргирови", "path": "Bulgarian/Естрада/Братя Аргирови", "songs": 5 }
  ],
  "files": []
}
```

`folders` are the immediate subfolders, sorted case-insensitively; `songs` counts every track
anywhere beneath the folder, not only the ones directly in it. `files` are the tracks sitting
directly in `path`, as ordinary discovery results — the same shape `Search` returns, so a browsed
track plays and queues exactly like a searched one. Only the local library is in the tree; there
are no `yt://` results here.

Leading, trailing and duplicated slashes are trimmed, and backslashes are read as separators. The
answer never touches the filesystem — the path is matched against the in-memory library's stored
locations — so a path nobody has, `..` included, is an empty folder rather than an error. There is
no failure response.

## Audio downloads and CORS

`GET /Audio/Download/{codec}/{bitrate}?id={id}` streams `Opus`, `Vorbis`, `FLAC`, `MP3`, or `AAC`; the frontend default is `/Audio/Download/Opus/112`. The stream advertises and supports standard HTTP `Range` requests; a seek request waits for the first cached encode to finish, then receives a normal `206` byte-range response. `DownloadRaw` is used by `contentUrl` and sends an attachment filename.

`GET /Audio/Preload/{codec}/{bitrate}?id={id}` starts that same encode without a body, so a Download that follows finds it already running. `202` means this call started it, `200` that it was already under way — repeats are cheap and only push the cache entry's expiry back. The frontend calls it 20 s before a track ends and when the skip button is hovered.

Preload is not just a nicety: it moves the encode for the next track into the current track's playtime, which is what stops a multiplayer room from stampeding the encoder every time everyone advances at once.

`Download`, `DownloadRaw` and `Preload` are served by the caching tier, which coalesces concurrent requests for the same `codec`/`bitrate`/`id` into a single encode and serves byte-range requests once an encode has finished. Range requests on an encode still in progress are answered `200` with `Accept-Ranges: none`; a completed one advertises `bytes` and answers `206`.

CORS allows the deployed `example.com` frontend (including subdomains) and `localhost`, `127.0.0.1`, and `::1` Vite origins at any port. Additional exact origins may be configured with `Cors:AllowedOrigins`. Production absolute links come from `PublicApiBaseUrl` (example: `https://api.example.com`).
