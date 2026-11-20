"""Rebuild the structured records from the page cache (no network).

The crawler stores every archived page it fetches, so parser improvements never need a re-crawl:
this script parses all cached pages again (in parallel) and writes cache/parsed/<kind>.jsonl,
which is what the snapshot builder reads.

Run:  .venv/Scripts/python reparse.py
"""
from __future__ import annotations

import json
import os
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

import archive
import parse

OUT = archive.CACHE / "parsed"


def _one(path: str) -> dict | None:
    try:
        data = json.loads(Path(path).read_text(encoding="utf-8"))
        if not data or "liquipedia.net/counterstrike/" not in data.get("url", ""):
            return None
        record = parse.parse(data["html"])
    except Exception:  # a malformed page is skipped, never fatal
        return None
    if record is not None:
        record["page"] = archive.title_of(data["url"])
        record["captured"] = data["captured"]
    return record


def main() -> None:
    files = [str(p) for p in (archive.CACHE / "pages").glob("*/*.json")]
    OUT.mkdir(parents=True, exist_ok=True)
    tmp = {k: (OUT / f"{k}.jsonl.tmp").open("w", encoding="utf-8") for k in ("tournament", "team", "player")}
    counts = dict.fromkeys(tmp, 0)
    with ProcessPoolExecutor(max_workers=max(1, (os.cpu_count() or 2) - 2)) as pool:
        for record in pool.map(_one, files, chunksize=32):
            if record is not None:
                tmp[record["kind"]].write(json.dumps(record, ensure_ascii=False) + "\n")
                counts[record["kind"]] += 1
    for k, f in tmp.items():
        f.close()
        (OUT / f"{k}.jsonl.tmp").replace(OUT / f"{k}.jsonl")
    print(f"{len(files)} cached pages -> {counts}")


if __name__ == "__main__":
    main()
