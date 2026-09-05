"""
One Spotify ``Track`` node as the pod result shape every other pod hands over -- no ``contentUrl`` and,
for this platform, no content at all: these are names for Gaida.API's resolver to look up somewhere
playable.

SpotAPI reaches three routes that wrap the same node three ways, so one mapper reads both spellings of
each field: ``getTrack`` gives ``duration`` and ``firstArtist``/``otherArtists``, while the playlist and
search queries give ``trackDuration`` and a single ``artists``.
"""

from typing import Any, Mapping

_UNKNOWN_TITLE = "Unknown title"
_UNKNOWN_ARTIST = "Unknown artist"


def to_dto(track: Mapping[str, Any] | None) -> dict[str, Any] | None:
    """The result DTO, or ``None`` for anything that is not a playable-catalogue track (a local file, an ad)."""
    if not isinstance(track, Mapping):
        return None

    identifier = _id(track)
    if not identifier:
        return None

    album = track.get("albumOfTrack") or {}
    return {
        "id": "spotify://" + identifier,
        "name": track.get("name") or _UNKNOWN_TITLE,
        "artist": _artists(track) or _UNKNOWN_ARTIST,
        "album": album.get("name"),
        "duration": _duration(track),
        "thumbnailUrl": _cover(album),
        "originalTitle": None,
        "originalArtist": None,
    }


def _id(track: Mapping[str, Any]) -> str | None:
    uri = track.get("uri") or ""
    if uri.startswith("spotify:track:"):
        return uri[len("spotify:track:"):]

    # The search and playlist nodes carry `id` only on the album, so a node without a track URI is not
    # a track -- an episode, a local file, or a removed entry Spotify still lists.
    return track.get("id") if track.get("__typename") == "Track" else None


def _artists(track: Mapping[str, Any]) -> str:
    names = []
    for group in ("artists", "firstArtist", "otherArtists"):
        for artist in (track.get(group) or {}).get("items") or []:
            name = (artist.get("profile") or {}).get("name")
            if name and name not in names:
                names.append(name)

    return ", ".join(names)


def _duration(track: Mapping[str, Any]) -> str:
    """
    The duration in the ``TimeSpan`` format Gaida.API parses back (``hh:mm:ss`` with an optional
    seven-digit fraction) -- the same text ``ToString("c")`` produced on the C# side.
    """
    node = track.get("trackDuration") or track.get("duration") or {}
    total = max(0, int(node.get("totalMilliseconds") or 0))

    days, rest = divmod(total, 86_400_000)
    hours, rest = divmod(rest, 3_600_000)
    minutes, rest = divmod(rest, 60_000)
    seconds, milliseconds = divmod(rest, 1000)

    clock = f"{hours:02d}:{minutes:02d}:{seconds:02d}"
    if days:
        clock = f"{days}.{clock}"

    return f"{clock}.{milliseconds * 10_000:07d}" if milliseconds else clock


def _cover(album: Mapping[str, Any]) -> str | None:
    """
    The largest cover Spotify offers. Sorting is not optional: this API returns its sources 300, 64, 640
    -- taking the first one would hand the client the middle size.
    """
    sources = [source for source in (album.get("coverArt") or {}).get("sources") or [] if source.get("url")]
    if not sources:
        return None

    return max(sources, key=lambda source: source.get("width") or 0)["url"]
