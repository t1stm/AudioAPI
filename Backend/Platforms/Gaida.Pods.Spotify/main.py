"""
The Spotify platform pod.

Spotify hands out metadata, never audio: every result here is a name for Gaida.API's resolver to look up
against the library or YouTube, which is why there is no ``/content`` route and no ``contentUrl`` in the
DTO. ``/random`` is 404 too -- a random Spotify track is a random resolve, and the pods that own audio
answer that question directly.

Backed by SpotAPI (https://github.com/Aran404/SpotAPI), which reaches Spotify's own web endpoints and
needs no client ID, no secret and no premium account. It is also unofficial: when Spotify changes those
endpoints the calls below start failing, and every one of them degrades to "found nothing" rather than
taking the route down.
"""

import json
import logging
import os
from typing import Any, Iterator

from fastapi import FastAPI, Response
from fastapi.responses import JSONResponse
from starlette.responses import StreamingResponse
from spotapi import Public

import admin
import classify
from mapper import to_dto

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(name)s: %(message)s")
log = logging.getLogger("gaida.spotify")

SEARCH_LIMIT = int(os.environ.get("SPOTIFY_SEARCH_LIMIT") or 15)
"""
How many search hits to hand back. Each one costs Gaida.API a resolve against the library or YouTube, so
this is a budget rather than a page size -- Spotify's own first page is 100.
"""

app = FastAPI(docs_url=None, redoc_url=None, openapi_url=None)

# The request ring is this pod's whole admin payload -- it holds no state an operator edits.
# No-op without ADMIN_TOKEN. See ADMIN_PLAN.md.
admin.install(app, lambda: {"service": "gaida-spotify", "searchLimit": SEARCH_LIMIT})


@app.get("/classify")
def classify_query(query: str | None = None) -> Response:
    result = classify.parse(query)
    if result.status == 200:
        return JSONResponse({"kind": result.kind, "id": result.id, "error": None})
    if result.status == 400:
        return JSONResponse({"kind": None, "id": None, "error": result.error}, status_code=400)

    return Response(status_code=404)


@app.get("/resolve")
def resolve(id: str | None = None) -> Response:
    track_id = classify.track_id(id or "")
    if not track_id:
        return Response(status_code=404)

    track = _ask(lambda: Public.song_info(track_id), f"track {track_id}")
    dto = to_dto(((track or {}).get("data") or {}).get("trackUnion"))
    return JSONResponse(dto) if dto else Response(status_code=404)


@app.get("/playlist")
def playlist(url: str | None = None) -> Response:
    playlist_id = classify.playlist_id(url or "")
    if not playlist_id:
        return JSONResponse([])

    # Streamed rather than assembled: Spotify pages a playlist a few hundred at a time and every track
    # then needs its own lookup upstream, so the sooner the first one leaves, the sooner that starts.
    return StreamingResponse(_json_array(_playlist_tracks(playlist_id)), media_type="application/json")


@app.get("/search")
def search(q: str | None = None) -> Response:
    query = (q or "").strip()
    if not query:
        return JSONResponse([])

    return JSONResponse([dto for dto in (to_dto((item.get("item") or {}).get("data"))
                                         for item in _search_hits(query)) if dto])


# Spotify has no audio and nothing to pick from at random. HttpPlatform reads both 404s as
# "route not supported" and moves on.
@app.get("/random")
@app.get("/content")
def unsupported() -> Response:
    return Response(status_code=404)


def _search_hits(query: str) -> list[dict[str, Any]]:
    """
    The first page of song hits, trimmed to :data:`SEARCH_LIMIT`.

    SpotAPI's generator pages 100 at a time, so the first ``next`` is exactly one request; closing it
    there returns the pooled client without asking for a second page nobody wants.
    """
    hits = _ask(lambda: next(iter(Public.song_search(query)), []), f"search {query!r}")
    return list(hits or [])[:SEARCH_LIMIT]


def _playlist_tracks(playlist_id: str) -> Iterator[dict[str, Any]]:
    """Playlist entries as Spotify pages them, mapped and with everything that is not a track dropped."""
    pages = _ask(lambda: Public.playlist_info(playlist_id), f"playlist {playlist_id}")
    if pages is None:
        return

    try:
        for page in pages:
            for item in page.get("items") or []:
                dto = to_dto((item.get("itemV2") or {}).get("data"))
                if dto:
                    yield dto
    except Exception:
        # Mid-stream: the entries already written stay written, which is the same contract the .NET
        # pods' Guarded() gives -- a short playlist rather than a torn response.
        log.warning("Spotify stopped paging playlist %s", playlist_id, exc_info=True)


def _json_array(items: Iterator[dict[str, Any]]) -> Iterator[bytes]:
    yield b"["
    for index, item in enumerate(items):
        yield (b"," if index else b"") + json.dumps(item).encode()
    yield b"]"


def _ask(call, what: str):
    """Runs one SpotAPI call, turning a refusal into ``None`` and a log line that names what was asked."""
    try:
        return call()
    except Exception as error:
        log.warning("Spotify refused %s: %s", what, error)
        return None
