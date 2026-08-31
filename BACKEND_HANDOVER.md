# Backend handover: Svelte frontend

## Current frontend state

The SvelteKit frontend is now the only active client. Steps 1–4 in
[`PLAN.md`](~/PhpStorm/AudioFrontend/PLAN.md) are complete: queue playback is stable, search results
are grouped by source, and the local queue dock is implemented. Steps 5–7
remain, with Step 5 blocked on confirming the mixed-query API contract.

The production API base currently used by the client is:

```
https://api.gergov.bg/Audio
```

During frontend verification, `FindQueryType` and a normal search request
returned HTTP 502. Please first restore a successful response for those public
read endpoints (and ensure CORS permits the deployed frontend and local Vite
development).

## Result contract shared by every discovery endpoint

The frontend expects a JSON array of these camel-case objects. Keep this
contract identical for search, random results, and both artist endpoints.

```ts
type SearchResult = {
	id: string;           // `audio://...` means a local-library result; every other ID is YouTube
	name: string;
	artist: string;       // YouTube channel name for YouTube results
	album?: string;       // Local-library album; omit or return null when unavailable
	contentUrl: string;   // Direct, downloadable source URL for “Download raw”
	duration: string;     // Parseable TimeSpan, for example `00:03:45`
	thumbnailUrl: string | null;
};
```

Please preserve the `audio://` local-ID prefix. The UI deliberately uses it to
label "In the library" results separately from "From YouTube" results.

## Endpoints already used by the frontend

| Endpoint | Current client use | Required behavior |
| --- | --- | --- |
| `GET /Audio/Search?query={term}` | `/search?term=` loader | Returns `SearchResult[]`; supports ordinary text, including Cyrillic. |
| `GET /Audio/RandomResults?count={count}` | Home-page curated picks | Returns `SearchResult[]`; `count=30` is live and `count=1`/`200` are needed next. |
| `GET /Audio/Download/{codec}/{bitrate}?id={id}` | HTML audio element | Streams a playable response for `Opus`, `Vorbis`, `FLAC`, `MP3`, or `AAC`; current default is `Opus/112`. |

Use standard URL query decoding: the frontend encodes `query` and `id` before
sending them. For streaming, range requests and correct audio content types are
important for seeking and browser media controls.

`contentUrl` is rendered as a normal download link in the search-row overflow
menu. It must therefore be a reachable absolute or same-origin URL and should
send a useful download filename/content disposition when possible.

## Required to unblock the next frontend work

### Artist rails and artist page

Implement and document these endpoints:

```text
GET /Audio/Artist/Local?term={artist}
GET /Audio/Artist/YouTube?term={artist}
```

Both should return the shared `SearchResult[]` schema, with a stable ordering.
The home page will request the local endpoint using the selected hero track’s
`artist`; `/artist?term=` will request both and display them in source tabs.
An unmatched artist should return `200 []`, not `404` or an HTML response.

### Mixed pasted queries

The home page will accept a YouTube video URL, a YouTube playlist URL, or an
`audio://` ID. The design direction identifies `FindQueryType` as the existing
server-side resolver, but its exact contract is not known to the frontend.

Please provide a reliable, documented endpoint—either the existing one or its
replacement—with:

```text
GET /Audio/FindQueryType?query={value}
```

Document the response shape and behavior for each input kind:

- local `audio://` ID;
- YouTube video URL/ID;
- YouTube playlist URL/ID;
- ordinary search text;
- malformed or unsupported input.

Prefer a machine-readable discriminator (for example `kind`) and enough data
to let the client add/play one track or enqueue a playlist. Return a consistent
JSON error body for invalid input; do not return a 502.

## Backend acceptance checklist

- `Search`, `RandomResults`, `Artist/Local`, and `Artist/YouTube` return the
  shared camel-case schema with valid JSON and no null `id`, `name`, `artist`,
  or `duration` values.
- Local results start with `audio://`; YouTube results do not.
- A Cyrillic search term returns correctly encoded JSON.
- `RandomResults?count=1`, `30`, and `200` are supported (or an explicit,
  documented maximum is enforced with a clear JSON validation error).
- Audio download plays in a browser and supports range seeking.
- `contentUrl`, thumbnail URLs, and download URLs are reachable from the
  browser under the frontend’s CORS policy.
- `FindQueryType` returns a documented successful response for every supported
  input kind and never leaks an upstream failure as an HTML/empty response.

## Frontend contacts / implementation locations

- Request wrappers: `src/requests/search.ts`, `src/requests/songs.ts`
- Result type: `src/state/search.svelte.ts`
- Audio URL construction: `src/state/current.svelte.ts`
- Search UI and raw-download action: `src/components/search/SearchRow.svelte`
- Pending home and artist integration: Steps 5 and 6 in `PLAN.md`

