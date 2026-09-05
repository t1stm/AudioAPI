"""
Recognising Deezer queries. Pure string parsing -- no network and no credentials, which is why this is
also the parser ``/playlist``, ``/resolve`` and ``/content`` reuse to pull an ID out of whatever form
they were given. The Spotify pod's classify.py is the same shape; only the alphabet differs.
"""

from dataclasses import dataclass
from urllib.parse import urlsplit

TRACK_SCHEME = "deezer://"
PLAYLIST_SCHEME = "deezer-playlist://"

_HOSTS = ("deezer.com",)
"""
Only the host that carries the ID in its path. Deezer's share links (deezer.page.link, dzr.page.link)
are redirects with nothing parseable in them, so they are left unclaimed and fall through to an
ordinary keyword search rather than being claimed and then failed.
"""


@dataclass(frozen=True)
class ClassifyResult:
    """
    Outcome of classifying one raw query: 200 (recognised), 400 (ours but malformed) or 404 (not ours --
    Gaida.API defaults that to a keyword search).
    """

    status: int
    kind: str | None = None
    id: str | None = None
    error: str | None = None


NOT_MINE = ClassifyResult(404)


def parse(value: str | None) -> ClassifyResult:
    """Classifies ``deezer://`` / ``deezer-playlist://`` ids and deezer.com track and playlist links."""
    query = (value or "").strip()
    if not query:
        return NOT_MINE

    lowered = query.lower()
    for prefix, classify in ((TRACK_SCHEME, _track), (PLAYLIST_SCHEME, _playlist)):
        if lowered.startswith(prefix):
            return classify(query[len(prefix):])

    link = urlsplit(query)
    host = link.hostname or ""
    if not any(host == known or host.endswith("." + known) for known in _HOSTS):
        return NOT_MINE

    # Locale-prefixed links are ordinary: deezer.com/en/track/ID, deezer.com/us/playlist/ID.
    segments = [segment for segment in link.path.split("/") if segment]
    for index in range(len(segments) - 1):
        if segments[index].lower() == "track":
            return _track(segments[index + 1])
        if segments[index].lower() == "playlist":
            return _playlist(segments[index + 1])

    return _invalid("The Deezer link is not a track or a playlist.")


def track_id(value: str) -> str | None:
    """The bare track ID out of anything :func:`parse` calls a track, or ``None``."""
    result = parse(value)
    if result.kind == "id" and result.id:
        return result.id[len(TRACK_SCHEME):]

    # Gaida.API strips the protocol before it asks (AudioManager.SearchID), so a bare ID is the
    # ordinary case on /resolve rather than an edge one.
    bare = value.strip()
    return bare if _is_id(bare) else None


def playlist_id(value: str) -> str | None:
    """The bare playlist ID out of anything :func:`parse` calls a playlist, or ``None``."""
    result = parse(value)
    return result.id[len(PLAYLIST_SCHEME):] if result.kind == "playlist" and result.id else None


def _track(value: str) -> ClassifyResult:
    return ClassifyResult(200, "id", TRACK_SCHEME + value) if _is_id(value) \
        else _invalid("The Deezer track ID is invalid.")


def _playlist(value: str) -> ClassifyResult:
    return ClassifyResult(200, "playlist", PLAYLIST_SCHEME + value) if _is_id(value) \
        else _invalid("The Deezer playlist ID is invalid.")


def _is_id(value: str) -> bool:
    """
    Deezer IDs are decimal. The length is not promised anywhere, so only the alphabet is enforced --
    but the leading '-' of a user-uploaded track is not accepted: nothing downstream can stream one.
    """
    return 0 < len(value) <= 20 and value.isascii() and value.isdigit()


def _invalid(message: str) -> ClassifyResult:
    return ClassifyResult(400, error=message)
