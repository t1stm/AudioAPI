#!/usr/bin/env python3
"""Remove entries listed in INVALID_FILE (from scan_dead_thumbnails.py) out of
YouTube.json. Re-checks the download dir in case a file was fetched since the
scan ran. Backs up the cache to /tmp before writing.
"""
import json
import os
import shutil
import time

CACHE_FILE = "/nvme0/DiscordBot/Cache/YouTube.json"
AUDIO_DIR = "/nvme0/DiscordBot/dll/audio"
INVALID_FILE = "/tmp/youtube_invalid_thumbnails.json"


def is_downloaded(entry_id):
    video_id = entry_id.split("://", 1)[-1]
    return os.path.exists(os.path.join(AUDIO_DIR, video_id + ".webm"))


def main():
    with open(INVALID_FILE) as f:
        invalid_ids = {e["ID"] for e in json.load(f)}

    with open(CACHE_FILE) as f:
        cache = json.load(f)

    backup_path = f"/tmp/YouTube.json.bak-{int(time.time())}"
    shutil.copy2(CACHE_FILE, backup_path)
    print(f"Backed up cache to {backup_path}")

    kept = []
    removed = 0
    for entry in cache:
        if entry["ID"] in invalid_ids and not is_downloaded(entry["ID"]):
            removed += 1
            print(f"removing {entry['ID']} - {entry.get('Name')}")
        else:
            kept.append(entry)

    tmp = CACHE_FILE + ".tmp"
    with open(tmp, "w") as f:
        json.dump(kept, f, indent=2, ensure_ascii=False)
    os.replace(tmp, CACHE_FILE)

    print(f"Removed {removed} entries, {len(kept)} remain")


if __name__ == "__main__":
    main()
