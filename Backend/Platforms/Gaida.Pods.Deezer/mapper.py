"""
One Deezer REST track object as the pod result shape every other pod hands over -- no ``contentUrl``,
which the pod cannot build because it does not know the public host.

Unlike the Spotify pod's mapper this one describes something playable: the ID it produces is what
``/content`` is later asked for. The three routes that produce tracks (``get_track``, ``search_track``,
``get_playlist_tracks``) all return the same object, so there is one spelling of every field.
"""

from typing import Any, Mapping

_UNKNOWN_TITLE = "Unknown title"
_UNKNOWN_ARTIST = "Unknown artist"


def to_dto(track: Mapping[str, Any] | None) -> dict[str, Any] | None:
    """The result DTO, or ``None`` for anything that is not a streamable catalogue track."""
    if not isinstance(track, Mapping):
        return None

    identifier = _id(track)
    if not identifier:
        return None

    album = track.get("album") or {}
    return {
        "id": "deezer://" + identifier,
        "name": track.get("title") or _UNKNOWN_TITLE,
        "artist": _artists(track) or _UNKNOWN_ARTIST,
        "album": album.get("title"),
        "duration": duration(track.get("duration")),
        "thumbnailUrl": _cover(album),
        "originalTitle": None,
        "originalArtist": None,
    }


def _id(track: Mapping[str, Any]) -> str | None:
    """
    The catalogue ID, or ``None`` for an entry nothing can stream.

    A playlist can hold a user's own upload, and Deezer gives those a negative ID that no CDN URL
    exists for. ``readable`` false is the same answer for a licensing reason rather than an upload one:
    either way the row would be a track that plays nothing.
    """
    identifier = str(track.get("id") or "").strip()
    if not identifier.isdigit() or track.get("readable") is False:
        return None

    return identifier


def _artists(track: Mapping[str, Any]) -> str:
    """
    The whole credit. ``artist`` is the lead alone and is all a search or playlist entry carries;
    ``contributors`` is the full list, already in Deezer's own order, and a feature that only appears
    there is exactly the name a listener searched for -- so it goes first when it is present.
    """
    names: list[str] = []
    for artist in (track.get("contributors") or []):
        name = (artist or {}).get("name")
        if name and name not in names:
            names.append(name)

    lead = (track.get("artist") or {}).get("name")
    if lead and lead not in names:
        names.insert(0, lead)

    return ", ".join(names)


def duration(seconds: Any) -> str:
    """
    Deezer's whole-second duration in the ``TimeSpan`` format Gaida.API parses back (``hh:mm:ss``) --
    the same text ``ToString("c")`` produces on the C# side.
    """
    try:
        total = max(0, int(seconds or 0))
    except (TypeError, ValueError):
        total = 0

    days, rest = divmod(total, 86_400)
    hours, rest = divmod(rest, 3_600)
    minutes, rest = divmod(rest, 60)

    clock = f"{hours:02d}:{minutes:02d}:{rest:02d}"
    return f"{days}.{clock}" if days else clock


def _cover(album: Mapping[str, Any]) -> str | None:
    """
    The largest cover Deezer offers, biggest first rather than by measuring: unlike Spotify's, these
    are named sizes in a fixed order, so the preference is a list and not a sort.
    """
    for key in ("cover_xl", "cover_big", "cover_medium", "cover_small", "cover"):
        url = album.get(key)
        if url:
            return url

    return None
