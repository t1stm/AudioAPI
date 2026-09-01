# Public Audio API

The production base URL is `https://api.gergov.bg`. All endpoints below are rooted at `/Audio` and use JSON unless they stream audio.

## Discovery result

`GET /Audio/Search?query={term}`, `GET /Audio/RandomResults?count={count}`, `GET /Audio/Artist/Local?term={artist}`, and `GET /Audio/Artist/YouTube?term={artist}` all return an array with exactly this shape:

```json
{
  "id": "audio://example-id",
  "name": "Track title",
  "artist": "Artist name",
  "album": "Album title",
  "contentUrl": "https://api.gergov.bg/Audio/DownloadRaw?id=audio%3A%2F%2Fexample-id",
  "duration": "00:03:45",
  "thumbnailUrl": null
}
```

`id`, `name`, `artist`, `contentUrl`, and `duration` are always non-null. Local-library IDs always begin with `audio://`; YouTube IDs use `yt://`. `album` and `thumbnailUrl` may be `null`. `contentUrl` is an absolute downloadable raw-audio URL.

- `Search` accepts normal text (including Cyrillic), local IDs, the server's `yt://` IDs, and playlist URLs.
- `RandomResults` accepts `count` from 1 through 200, inclusive. An invalid count returns `400 {"error":{"code":"invalid_count","message":"..."}}`.
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

## Audio downloads and CORS

`GET /Audio/Download/{codec}/{bitrate}?id={id}` streams `Opus`, `Vorbis`, `FLAC`, `MP3`, or `AAC`; the frontend default is `/Audio/Download/Opus/112`. The stream advertises and supports standard HTTP `Range` requests; a seek request waits for the first cached encode to finish, then receives a normal `206` byte-range response. `DownloadRaw` is used by `contentUrl` and sends an attachment filename.

CORS allows the deployed `gergov.bg` frontend (including subdomains) and `localhost`, `127.0.0.1`, and `::1` Vite origins at any port. Additional exact origins may be configured with `Cors:AllowedOrigins`. Production absolute links come from `PublicApiBaseUrl` (default `https://api.gergov.bg`).
