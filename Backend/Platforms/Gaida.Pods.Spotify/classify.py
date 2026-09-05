"""
Recognising Spotify queries. Pure string parsing -- no network and no credentials, which is why this is
also the parser ``/playlist`` and ``/resolve`` reuse to pull an ID out of whatever form they were given.
"""

from dataclasses import dataclass
from urllib.parse import urlsplit

TRACK_SCHEME = "spotify://"
PLAYLIST_SCHEME = "spotify-playlist://"

_HOSTS = ("spotify.com", "spotify.link")


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
    """Classifies ``spotify://`` / ``spotify-playlist://`` ids, ``spotify:track:...`` URIs and Spotify links."""
    query = (value or "").strip()
    if not query:
        return NOT_MINE

    lowered = query.lower()
    for prefix, classify in ((TRACK_SCHEME, _track), (PLAYLIST_SCHEME, _playlist),
                             # The URI form the desktop client copies.
                             ("spotify:track:", _track), ("spotify:playlist:", _playlist)):
        if lowered.startswith(prefix):
            return classify(query[len(prefix):])

    link = urlsplit(query)
    host = link.hostname or ""
    if not any(host == known or host.endswith("." + known) for known in _HOSTS):
        return NOT_MINE

    # Locale-prefixed links are ordinary: open.spotify.com/intl-de/track/ID.
    segments = [segment for segment in link.path.split("/") if segment]
    for index in range(len(segments) - 1):
        if segments[index].lower() == "track":
            return _track(segments[index + 1])
        if segments[index].lower() == "playlist":
            return _playlist(segments[index + 1])

    return _invalid("The Spotify link is not a track or a playlist.")


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
        else _invalid("The Spotify track ID is invalid.")


def _playlist(value: str) -> ClassifyResult:
    return ClassifyResult(200, "playlist", PLAYLIST_SCHEME + value) if _is_id(value) \
        else _invalid("The Spotify playlist ID is invalid.")


def _is_id(value: str) -> bool:
    """Spotify's base-62 IDs are 22 characters; the length is not promised, so only the alphabet is enforced."""
    return 10 <= len(value) <= 64 and value.isascii() and value.isalnum()


def _invalid(message: str) -> ClassifyResult:
    return ClassifyResult(400, error=message)
