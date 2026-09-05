"""
ponytail: the one runnable check for this pod's pure logic -- the parsing, the mapping, the vendored
key derivation and the cache's two rules that would be wrong silently. No fixtures, no network, no
ARL. Run with `pytest`, or with `python test_deezer.py` in the pod image, which has neither.
"""

import json
import tempfile
from pathlib import Path

import cache
import classify
import stream
from mapper import duration, to_dto

TRACK = "3135556"
PLAYLIST = "908622995"


# ── classify ────────────────────────────────────────────────────────────────────────────────────

def test_recognises_tracks():
    for query in (f"deezer://{TRACK}",
                  f"https://www.deezer.com/track/{TRACK}",
                  f"https://www.deezer.com/en/track/{TRACK}?utm_source=x",
                  f"https://deezer.com/us/track/{TRACK}"):
        assert classify.parse(query) == classify.ClassifyResult(200, "id", f"deezer://{TRACK}"), query


def test_recognises_playlists():
    for query in (f"deezer-playlist://{PLAYLIST}",
                  f"https://www.deezer.com/playlist/{PLAYLIST}",
                  f"https://www.deezer.com/fr/playlist/{PLAYLIST}"):
        assert classify.parse(query) == classify.ClassifyResult(
            200, "playlist", f"deezer-playlist://{PLAYLIST}"), query


def test_claims_but_rejects_malformed():
    assert classify.parse("deezer://not-a-number") == classify.ClassifyResult(
        400, error="The Deezer track ID is invalid.")
    assert classify.parse("https://www.deezer.com/album/302127") == classify.ClassifyResult(
        400, error="The Deezer link is not a track or a playlist.")


def test_leaves_other_queries_alone():
    # A share link carries no ID, so claiming it would mean claiming a query this pod cannot answer.
    for query in ("", "   ", "daft punk", "yt://dQw4w9WgXcQ", "https://deezer.page.link/abcdef",
                  "https://open.spotify.com/track/4cOdK2wGLETKBW3PvgPWqT"):
        assert classify.parse(query) == classify.NOT_MINE, query


def test_ids_come_back_bare():
    assert classify.track_id(f"https://www.deezer.com/track/{TRACK}") == TRACK
    # Gaida.API strips the protocol before it asks, so a bare ID is the ordinary case on /resolve.
    assert classify.track_id(TRACK) == TRACK
    assert classify.track_id("audio://abc") is None
    assert classify.playlist_id(f"deezer-playlist://{PLAYLIST}") == PLAYLIST
    assert classify.playlist_id(f"deezer://{TRACK}") is None


# ── mapper ──────────────────────────────────────────────────────────────────────────────────────

def _track(**overrides):
    track = {
        "id": 3135556,
        "title": "Harder, Better, Faster, Stronger",
        "duration": 224,
        "artist": {"name": "Daft Punk"},
        "album": {"title": "Discovery", "cover_small": "small.jpg", "cover_xl": "xl.jpg"},
    }
    track.update(overrides)
    return track


def test_maps_a_track():
    dto = to_dto(_track())
    assert dto == {
        "id": "deezer://3135556",
        "name": "Harder, Better, Faster, Stronger",
        "artist": "Daft Punk",
        "album": "Discovery",
        "duration": "00:03:44",
        # The biggest cover, not the first one -- a small one here is a blurry row in every client.
        "thumbnailUrl": "xl.jpg",
        "originalTitle": None,
        "originalArtist": None,
    }


def test_drops_what_cannot_be_played():
    assert to_dto(None) is None
    assert to_dto({"title": "no id"}) is None
    # A user's own upload has a negative ID that no CDN URL exists for.
    assert to_dto(_track(id=-4258)) is None
    # Unreadable here means licensing rather than uploads, but the row plays nothing either way.
    assert to_dto(_track(readable=False)) is None


def test_credits_every_contributor():
    dto = to_dto(_track(contributors=[{"name": "Daft Punk"}, {"name": "Pharrell Williams"}]))
    assert dto["artist"] == "Daft Punk, Pharrell Williams"
    # The lead is kept even when Deezer left it out of the contributor list.
    assert to_dto(_track(contributors=[{"name": "Pharrell Williams"}]))["artist"] == \
        "Daft Punk, Pharrell Williams"


def test_duration_is_the_timespan_gaida_parses():
    assert duration(0) == "00:00:00"
    assert duration(224) == "00:03:44"
    assert duration(3661) == "01:01:01"
    assert duration(90061) == "1.01:01:01"
    # Deezer occasionally omits it; a missing duration is zero, never a crash.
    assert duration(None) == "00:00:00"
    assert duration(-5) == "00:00:00"


# ── the vendored crypto ─────────────────────────────────────────────────────────────────────────

def test_blowfish_key_matches_upstream():
    """
    The key derivation is the one piece of streamrip that fails silently: a wrong key decrypts to
    noise, which is a file that downloads, caches and plays as static. Checked against the derivation
    spelled out rather than against the implementation, so a typo in either shows up here.
    """
    import hashlib

    digest = hashlib.md5(TRACK.encode()).hexdigest()
    expected = bytes(ord(a) ^ ord(b) ^ ord(c)
                     for a, b, c in zip(digest[:16], digest[16:], "g4el58wc0zvf9na1"))

    assert stream.blowfish_key(TRACK) == expected
    assert len(stream.blowfish_key(TRACK)) == 16


def test_decrypt_leaves_the_clear_stride_alone():
    """
    Deezer encrypts the first 2048 bytes of every 6144 and a short tail not at all. Round-tripping
    real Blowfish over a synthetic file proves the strides line up -- an off-by-one here corrupts one
    block in three of every song.
    """
    from Cryptodome.Cipher import Blowfish

    key = stream.blowfish_key(TRACK)
    plain = bytes(index % 251 for index in range(6144 + 2048 + 100))

    encrypted = bytearray(plain)
    for start in (0, 6144):
        cipher = Blowfish.new(key, Blowfish.MODE_CBC, b"\x00\x01\x02\x03\x04\x05\x06\x07")
        encrypted[start:start + 2048] = cipher.encrypt(plain[start:start + 2048])

    assert stream._decrypt(TRACK, bytes(encrypted)) == plain


# ── cache ───────────────────────────────────────────────────────────────────────────────────────

def _dto(name="Harder, Better, Faster, Stronger"):
    return {"name": name, "artist": "Daft Punk", "album": "Discovery",
            "duration": "00:03:44", "thumbnailUrl": "xl.jpg"}


def test_cache_upgrade_replaces_the_old_format():
    """A promote must not leave the MP3 behind — two copies of every promoted track is the cache cap."""
    with tempfile.TemporaryDirectory() as directory:
        songs = cache.Cache(directory, cache.MAX_BYTES_DEFAULT)
        songs.store(TRACK, b"x" * 10, stream.MP3, _dto())
        assert (Path(directory) / f"{TRACK}.mp3").exists()

        entry = songs.store(TRACK, b"y" * 20, stream.FLAC, _dto())
        assert entry.format == stream.FLAC
        assert not (Path(directory) / f"{TRACK}.mp3").exists()
        assert (Path(directory) / f"{TRACK}.flac").read_bytes() == b"y" * 20
        assert songs.stats() == (1, 20)


def test_cache_evicts_oldest_first():
    with tempfile.TemporaryDirectory() as directory:
        songs = cache.Cache(directory, 25)
        for index, track in enumerate(("1", "2", "3")):
            songs.store(track, b"z" * 10, stream.MP3, _dto())

        # 30 bytes stored against a 25-byte cap, so exactly the oldest one goes.
        assert songs.get("1") is None
        assert songs.get("2") is not None and songs.get("3") is not None
        assert not (Path(directory) / "1.mp3").exists()
        assert not (Path(directory) / "1.json").exists()


def test_cache_survives_a_restart():
    with tempfile.TemporaryDirectory() as directory:
        cache.Cache(directory, cache.MAX_BYTES_DEFAULT).store(TRACK, b"x" * 10, stream.FLAC, _dto())

        reopened = cache.Cache(directory, cache.MAX_BYTES_DEFAULT)
        entry = reopened.get(TRACK)
        assert entry is not None and entry.format == stream.FLAC
        assert entry.to_dto()["id"] == f"deezer://{TRACK}"

        # A sidecar whose audio was deleted underneath us is not a cache hit, and does not linger.
        (Path(directory) / f"{TRACK}.flac").unlink()
        again = cache.Cache(directory, cache.MAX_BYTES_DEFAULT)
        assert again.get(TRACK) is None
        assert not (Path(directory) / f"{TRACK}.json").exists()


def test_cache_recent_caps_newest_first_and_0_lifts_the_cap():
    with tempfile.TemporaryDirectory() as directory:
        songs = cache.Cache(directory, cache.MAX_BYTES_DEFAULT)
        for track in ("1", "2", "3"):
            songs.store(track, b"z" * 10, stream.MP3, _dto())

        assert [entry.id for entry in songs.recent(2)] == ["3", "2"]
        assert [entry.id for entry in songs.recent(0)] == ["3", "2", "1"]


def test_cache_ignores_a_torn_sidecar():
    with tempfile.TemporaryDirectory() as directory:
        (Path(directory) / "9.json").write_text("{not json")
        (Path(directory) / "8.json").write_text(json.dumps({"id": "8"}))  # missing every other field

        songs = cache.Cache(directory, cache.MAX_BYTES_DEFAULT)
        assert songs.stats() == (0, 0)


if __name__ == "__main__":
    for name, test in sorted(dict(globals()).items()):
        if name.startswith("test_"):
            test()
            print(f"  {name} ok")

    print("selftest OK")
