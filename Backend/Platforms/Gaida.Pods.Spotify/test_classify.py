"""
ponytail: the one runnable check for the parsing and the mapping -- the cases the C# pod's
ClassifySelfCheck covered, plus the two things the DTO gets wrong if nobody looks. No fixtures and no
network. Run with `pytest`, or with `python test_classify.py` in the pod image, which has neither.
"""

import classify
from mapper import to_dto

TRACK = "4cOdK2wGLETKBW3PvgPWqT"
PLAYLIST = "37i9dQZF1DXcBWIGoYBM5M"


def test_recognises_tracks():
    for query in (f"spotify://{TRACK}", f"spotify:track:{TRACK}",
                  f"https://open.spotify.com/track/{TRACK}?si=abc",
                  f"https://open.spotify.com/intl-de/track/{TRACK}"):
        assert classify.parse(query) == classify.ClassifyResult(200, "id", f"spotify://{TRACK}"), query


def test_recognises_playlists():
    for query in (f"spotify-playlist://{PLAYLIST}", f"spotify:playlist:{PLAYLIST}",
                  f"https://open.spotify.com/playlist/{PLAYLIST}"):
        assert classify.parse(query) == classify.ClassifyResult(200, "playlist",
                                                                f"spotify-playlist://{PLAYLIST}"), query


def test_claims_but_rejects_malformed():
    assert classify.parse("spotify://short") == classify.ClassifyResult(
        400, error="The Spotify track ID is invalid.")
    assert classify.parse(f"https://open.spotify.com/album/{TRACK}") == classify.ClassifyResult(
        400, error="The Spotify link is not a track or a playlist.")


def test_leaves_everything_else_alone():
    # Another platform's link, plain text, nothing at all.
    for query in ("https://youtu.be/dQw4w9WgXcQ", "hello world search text", "", None):
        assert classify.parse(query) == classify.NOT_MINE, query


def test_ids_out_of_every_form():
    assert classify.track_id(f"spotify://{TRACK}") == TRACK
    # What Gaida.API actually sends /resolve: AudioManager.SearchID strips the protocol first.
    assert classify.track_id(TRACK) == TRACK
    assert classify.track_id(f"https://open.spotify.com/playlist/{PLAYLIST}") is None
    assert classify.playlist_id(f"spotify:playlist:{PLAYLIST}") == PLAYLIST
    assert classify.playlist_id(f"spotify://{TRACK}") is None


def test_maps_a_track():
    dto = to_dto({
        "__typename": "Track",
        "uri": f"spotify:track:{TRACK}",
        "name": "Never Gonna Give You Up",
        "duration": {"totalMilliseconds": 213573},
        "firstArtist": {"items": [{"profile": {"name": "Rick Astley"}}]},
        "otherArtists": {"items": [{"profile": {"name": "Someone Else"}}]},
        "albumOfTrack": {
            "name": "Whenever You Need Somebody",
            # Spotify returns these 300, 64, 640 -- the first one is not the biggest.
            "coverArt": {"sources": [{"url": "medium", "width": 300}, {"url": "small", "width": 64},
                                     {"url": "large", "width": 640}]},
        },
    })

    assert dto == {
        "id": f"spotify://{TRACK}",
        "name": "Never Gonna Give You Up",
        "artist": "Rick Astley, Someone Else",
        "album": "Whenever You Need Somebody",
        # TimeSpan's "c" format, which is what Gaida.API parses back.
        "duration": "00:03:33.5730000",
        "thumbnailUrl": "large",
        "originalTitle": None,
        "originalArtist": None,
    }


def test_maps_the_playlist_and_search_spelling():
    dto = to_dto({
        "__typename": "Track",
        "uri": f"spotify:track:{TRACK}",
        "name": "Ain't In LA",
        "trackDuration": {"totalMilliseconds": 184000},
        "artists": {"items": [{"profile": {"name": "ADÉLA"}}]},
    })

    assert dto is not None
    assert dto["duration"] == "00:03:04"
    assert dto["artist"] == "ADÉLA"
    assert dto["album"] is None and dto["thumbnailUrl"] is None


def test_drops_what_is_not_a_track():
    # Spotify lists removed entries, local files and episodes inside playlists; none of them resolve.
    assert to_dto(None) is None
    assert to_dto({}) is None
    assert to_dto({"__typename": "Episode", "id": "abc", "name": "An episode"}) is None


if __name__ == "__main__":
    for name, check in sorted(globals().items()):
        if name.startswith("test_"):
            check()
            print(f"{name}: OK")
