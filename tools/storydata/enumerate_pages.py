"""Step 1: list every Liquipedia Counter-Strike page the Internet Archive holds.

Pages through the CDX index (collapsed by URL, successful captures only) and writes
cache/urls.jsonl, one {"url", "title", "last"} per distinct page. Resumable: the CDX resume key
is saved after every batch, so the script can be stopped and started again.

Run:  .venv/Scripts/python enumerate_pages.py
"""
from __future__ import annotations

import json
import urllib.parse

import archive

OUT = archive.CACHE / "urls.jsonl"
STATE = archive.CACHE / "enumerate.state.json"
BATCH = 5000

# Subpages and namespaces that are not team / player / tournament pages.
SKIP_PARTS = ("Special:", "File:", "Template:", "Category:", "User:", "Liquipedia:", "Help:", "Talk:",
              "index.php", "api.php", "?", "#")


def wanted(url: str) -> bool:
    path = urllib.parse.urlsplit(url).path
    if "/counterstrike/" not in path:
        return False
    rest = path.split("/counterstrike/", 1)[1]
    return bool(rest) and not any(p in rest for p in SKIP_PARTS)


def main() -> None:
    archive.CACHE.mkdir(parents=True, exist_ok=True)
    state = json.loads(STATE.read_text()) if STATE.exists() else {"resume": None, "count": 0, "done": False}
    if state.get("done"):
        print(f"already complete: {state['count']} pages in {OUT}")
        return
    seen = set()
    if OUT.exists():
        for line in OUT.read_text(encoding="utf-8").splitlines():
            seen.add(json.loads(line)["title"])

    with OUT.open("a", encoding="utf-8") as out:
        while True:
            params = {
                "url": "liquipedia.net/counterstrike/", "matchType": "prefix", "collapse": "urlkey",
                "filter": "statuscode:200", "fl": "original,timestamp", "limit": BATCH, "showResumeKey": "true",
            }
            if state["resume"]:
                params["resumeKey"] = state["resume"]
            rows = archive.cdx(params)
            resume = None
            # With showResumeKey the last row is the key, preceded by an empty row.
            if len(rows) >= 2 and len(rows[-1]) == 1:
                resume = rows[-1][0]
                rows = rows[:-2] if rows[-2] == [] else rows[:-1]
            for row in rows[1:] if rows and rows[0] == ["original", "timestamp"] else rows:
                if len(row) < 2 or not wanted(row[0]):
                    continue
                title = archive.title_of(row[0])
                if title in seen:
                    continue
                seen.add(title)
                out.write(json.dumps({"url": row[0], "title": title, "last": row[1]}) + "\n")
            out.flush()
            state["resume"] = resume
            state["count"] = len(seen)
            state["done"] = resume is None
            STATE.write_text(json.dumps(state))
            print(f"{len(seen)} distinct pages so far", flush=True)
            if resume is None:
                break
    print(f"done: {len(seen)} pages in {OUT}")


if __name__ == "__main__":
    main()
