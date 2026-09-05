"""
The admin surface every Gaida service answers: the shared-secret check, the request ring and the live
feed. A port of Gaida.Admin/AdminApi.cs, kept field-for-field so Oko cannot tell this pod apart from the
.NET ones -- see ADMIN_PLAN.md.

Everything here is pull: the pod answers when asked and pushes nothing. Nothing in this module knows the
admin panel's address, or that it exists.
"""

import asyncio
import hmac
import json
import logging
import os
import time
from collections import deque
from datetime import datetime, timezone
from typing import Any, AsyncIterator, Callable

from fastapi import APIRouter, Depends, FastAPI, Header, HTTPException
from fastapi.responses import StreamingResponse

log = logging.getLogger("gaida.admin")

TOKEN_HEADER = "X-Admin-Token"
"""The header Oko authenticates itself with. Not `Authorization` -- that one is the operator's."""

CAPACITY = 500
"""~50 KB. Long enough to see what just happened, short enough to never be a memory question."""


class Feed:
    """
    The last :data:`CAPACITY` requests, plus a live fan-out of them to whoever is watching.

    Bounded in both directions, which is what makes an unwatched panel free: the ring wraps in place and
    never grows, and the per-subscriber queues only exist while a subscriber does. No lock -- every write
    happens in the middleware, which runs on the event loop.
    """

    def __init__(self) -> None:
        self.recent: deque[dict[str, Any]] = deque(maxlen=CAPACITY)
        self.subscribers: list[asyncio.Queue] = []

    def record(self, entry: dict[str, Any]) -> None:
        self.recent.append(entry)
        for queue in self.subscribers:
            # Drop-oldest rather than block: a subscriber that has stopped reading costs the request
            # path nothing. This is the whole reason the panel pulls.
            if queue.full():
                queue.get_nowait()
            queue.put_nowait(entry)

    async def subscribe(self) -> AsyncIterator[str]:
        """Requests as they happen, as SSE frames, for as long as the caller stays connected."""
        queue: asyncio.Queue = asyncio.Queue(maxsize=256)
        self.subscribers.append(queue)
        try:
            while True:
                entry = await queue.get()
                yield f"event: request\ndata: {json.dumps(entry)}\n\n"
        finally:
            self.subscribers.remove(queue)


def install(app: FastAPI, snapshot: Callable[[], Any]) -> None:
    """
    Installs the request ring and maps ``/Admin/snapshot``, ``/Admin/requests`` and ``/Admin/events``.

    Does nothing at all when ``ADMIN_TOKEN`` is unset. Fail closed: no secret, no admin surface.

    :param snapshot: whatever this pod wants an operator to see. Called per request and never cached:
        the pod's own state is the only copy, and a second one would just be staler.
    """
    token = (os.environ.get("ADMIN_TOKEN") or "").strip()
    if not token:
        log.info("ADMIN_TOKEN is not set - the /Admin surface is disabled.")
        return

    feed = Feed()
    expected = token.encode()

    @app.middleware("http")
    async def ring(request, call_next):
        # The admin routes are excluded from their own ring on purpose: /Admin/events recording itself
        # would feed every SSE frame back into the stream that produced it.
        if request.url.path.startswith("/Admin"):
            return await call_next(request)

        started = time.perf_counter()
        response = await call_next(request)
        query = request.url.query
        feed.record({
            "at": datetime.now(timezone.utc).isoformat(),
            "method": request.method,
            # Streaming routes are timed to their headers, not their last byte -- the body is still
            # being produced when this runs.
            "path": request.url.path + ("?" + query if query else ""),
            "status": response.status_code,
            "ms": int((time.perf_counter() - started) * 1000),
        })
        return response

    # 404, not 401: a scanner without the token cannot tell the surface is there at all.
    def authorize(given: str = Header(default="", alias=TOKEN_HEADER)) -> None:
        if not hmac.compare_digest(expected, given.encode()):
            raise HTTPException(status_code=404)

    router = APIRouter(prefix="/Admin", dependencies=[Depends(authorize)])

    @router.get("/snapshot")
    def read_snapshot() -> Any:
        return snapshot()

    @router.get("/requests")
    def read_requests() -> list[dict[str, Any]]:
        return list(feed.recent)

    @router.get("/events")
    def read_events() -> StreamingResponse:
        return StreamingResponse(feed.subscribe(), media_type="text/event-stream")

    app.include_router(router)
