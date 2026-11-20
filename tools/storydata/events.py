"""Step 4: Historical Events from the archive: bans and permanent leaves (GLOSSARY).

  * Bans: the banned-player lists (Valve, ESIC, other organisers). Overturned bans are left out.
    "Permanent" bans stay permanent; Valve's 2021 amnesty for old VAC bans is kept as the end date
    the lists give.
  * Deaths: only facts recorded on player pages ("Died"), as agreed (ADR 0002): Story Mode never
    invents them.

Writes out/events.generated.json, sorted by date. The curated Game Updates (game versions, map
pool, balance patches) live in data/story/game_updates.json and are merged by the loader.

Run:  .venv/Scripts/python events.py
"""
from __future__ import annotations

import json
import re

from bs4 import BeautifulSoup

import archive
import crawl
import parse

OUT = archive.CACHE.parent / "out"
BAN_PAGES = {"Banned Players/Valve": "Valve", "Banned Players/ESIC": "ESIC",
             "Banned Players/Other": "organiser", "Banned Players/Other/Former": "organiser"}


def bans() -> list[dict]:
    out = []
    for title, issuer in BAN_PAGES.items():
        try:
            p = archive.page(crawl.page_url(title))
        except archive.FetchFailed:
            p = None  # the Archive is busy: the page comes with the next run
        if not p:
            print(f"  (no archived copy of {title})")
            continue
        soup = BeautifulSoup(p["html"], "lxml")
        for row in soup.select(".divRow"):
            name = row.select_one(".divCell.Name")
            if name is None:
                continue
            section = parse.clean(row.find_previous(["h2", "h3"]).get_text(" ")) if row.find_previous(["h2", "h3"]) else ""
            if "overturned" in section.lower():
                continue
            cells = [parse.clean(c.get_text(" ")) for c in row.select(".divCell")]
            dates = [parse.clean(c.get_text(" ")) for c in row.select(".divCell.Date")]
            kind = next((c for c in cells if re.search(r"cheat|fix|bug|abuse|bet|toxic|conduct|violat", c, re.I)), "")
            team_cell = row.select_one(".divCell.Team a")
            for a in name.select("a"):
                if a.find_parent(class_="flag") is not None or "Category:" in a.get("href", ""):
                    continue
                out.append({
                    "type": "ban", "date": parse.date(dates[0]) if dates else None,
                    "until": "permanent" if len(dates) > 1 and "permanent" in dates[1].lower() else (parse.date(dates[1]) if len(dates) > 1 else None),
                    "until_text": dates[1] if len(dates) > 1 else None,
                    "player": parse.clean(a.get_text(" ")), "page": parse.link_title(a),
                    "team": parse.link_title(team_cell) if team_cell else None,
                    "reason": kind, "issuer": issuer, "source": title,
                })
    return [b for b in out if b["date"]]


def deaths() -> list[dict]:
    out = []
    path = archive.CACHE / "parsed" / "player.jsonl"
    if not path.exists():
        return out
    for line in path.read_text(encoding="utf-8").splitlines():
        r = json.loads(line)
        if r.get("died"):
            out.append({"type": "permanent_leave", "reason": "death", "date": r["died"], "player": r["id"],
                        "page": r["page"], "source": r["page"]})
    return out


def main() -> None:
    events = bans() + deaths()
    events.sort(key=lambda e: e["date"])
    OUT.mkdir(exist_ok=True)
    (OUT / "events.generated.json").write_text(json.dumps(events, ensure_ascii=False, indent=1), encoding="utf-8")
    kinds = {}
    for e in events:
        kinds[e["type"]] = kinds.get(e["type"], 0) + 1
    print(f"{len(events)} events {kinds} -> {OUT / 'events.generated.json'}")


if __name__ == "__main__":
    main()
