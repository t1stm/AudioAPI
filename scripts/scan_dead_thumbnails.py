#!/usr/bin/env python3
"""Scan YouTube.json for entries whose thumbnail 404s. Writes matches to
INVALID_FILE (doesn't touch the cache). Resumable across runs, rate-limited,
stops on HTTP 429. Run it again later to continue where it left off.
"""
import json
import os
import time
import urllib.error
import urllib.request

CACHE_FILE = "/nvme0/DiscordBot/Cache/YouTube.json"
AUDIO_DIR = "/nvme0/DiscordBot/dll/audio"
STATE_FILE = "/tmp/youtube_thumb_scan_state.json"
INVALID_FILE = "/tmp/youtube_invalid_thumbnails.json"
RATE_LIMIT_SECONDS = 0.4  # ~2.5 req/sec
CHECKPOINT_EVERY = 200  # requests made between progress saves


def load_json(path, default):
    if os.path.exists(path):
        with open(path) as f:
            return json.load(f)
    return default


def save_json(path, data):
    tmp = path + ".tmp"
    with open(tmp, "w") as f:
        json.dump(data, f, indent=2)
    os.replace(tmp, path)


def is_downloaded(entry_id):
    video_id = entry_id.split("://", 1)[-1]
    return os.path.exists(os.path.join(AUDIO_DIR, video_id + ".webm"))


def thumb_is_404(url):
    req = urllib.request.Request(url, method="HEAD")
    try:
        urllib.request.urlopen(req, timeout=10)
        return False
    except urllib.error.HTTPError as e:
        if e.code == 429:
            raise
        return e.code == 404
    except urllib.error.URLError:
        return False  # transient network issue, don't flag


def main():
    cache = load_json(CACHE_FILE, [])
    state = load_json(STATE_FILE, {"next_index": 0})
    invalid = load_json(INVALID_FILE, [])
    invalid_ids = {e["ID"] for e in invalid}

    start = state["next_index"]
    next_index = len(cache)
    requests_made = 0
    print(f"Resuming at {start}/{len(cache)}, {len(invalid)} already flagged invalid")

    for i in range(start, len(cache)):
        entry = cache[i]
        eid = entry["ID"]
        if eid in invalid_ids or is_downloaded(eid):
            continue

        try:
            dead = thumb_is_404(entry["ThumbnailUrl"])
        except urllib.error.HTTPError:
            print(f"[{i}] Hit 429, stopping for now.")
            next_index = i
            break

        requests_made += 1
        if dead:
            invalid.append(entry)
            invalid_ids.add(eid)
            print(f"[{i}] DEAD: {eid} - {entry.get('Name')}")
        else:
            print(f"[{i}/{len(cache)}] ok: {eid}")

        time.sleep(RATE_LIMIT_SECONDS)

        if requests_made % CHECKPOINT_EVERY == 0:
            save_json(INVALID_FILE, invalid)
            save_json(STATE_FILE, {"next_index": i + 1})

    save_json(INVALID_FILE, invalid)
    save_json(STATE_FILE, {"next_index": next_index})
    print(f"Stopped at {next_index}/{len(cache)}. {len(invalid)} invalid entries in {INVALID_FILE}")


if __name__ == "__main__":
    main()
