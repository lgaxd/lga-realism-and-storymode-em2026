"""The historical tournament calendar for Story Mode (ADR 0006), from the parsed archive.

Every S, A, B and C-Tier event of the Main Circuit from 2000 to the last full year of data, with
its real dates, prize pool, LAN or online, and venue. The game's tiers: Valve Majors -> Major,
S -> T1, A -> T2, B -> T3, C -> T4 (old pages call the tiers Premier, Major and Minor).
Left out: CS:Source, national-team and female-only events (the game has no such restrictions),
qualifiers, weeklies and monthlies, showmatches, and events that were cancelled (listed by hand:
the archive cannot tell them from pages it failed to read).

Run:  .venv/Scripts/python tour_calendar.py            (writes out/calendar.json)
      .venv/Scripts/python tour_calendar.py --install  (and puts it in the game's story folder)
"""
from __future__ import annotations

import json
import re
import shutil
import sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).parent
PARSED = HERE / "cache" / "parsed" / "tournament.jsonl"
OUT = HERE / "out" / "calendar.json"
GAME_STORY = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Esports Manager 2026\BepInEx\plugins\RealismOverhaul\story")
LAST_YEAR = 2026

TIERS = {"s-tier": "T1", "premier": "T1", "a-tier": "T2", "major": "T2", "b-tier": "T3", "minor": "T3", "c-tier": "T4"}
# Valve Majors whose name does not say so.
EARLY_MAJORS = {"DreamHack Winter 2013", "ESL Major Series One Katowice 2014", "ESL One: Cologne 2014",
                "DreamHack Winter 2014", "ESL One: Katowice 2015", "ESL One: Cologne 2015",
                "DreamHack Open Cluj-Napoca 2015", "ESL One: Cologne 2016"}
# Announced, then called off (the archive cannot tell a cancelled event from a page it failed to read).
CANCELLED = {"ESL One: Rio Major 2020"}
# "CS2 Major Championship Winter 2025" pages only point at that season's Major.
OVERVIEW = re.compile(r"(CS:GO|CS2) Major Championship")


def is_major(r: dict) -> bool:
    name = r.get("name") or ""
    if name in EARLY_MAJORS:
        return True
    return (r.get("tier") == "S-Tier" and re.search(r"\bMajor\b", name) is not None
            and "Major Series" not in name)


def events() -> list[dict]:
    best: dict[tuple, dict] = {}
    for line in PARSED.read_text(encoding="utf-8").splitlines():
        r = json.loads(line)
        tier = TIERS.get((r.get("tier") or "").strip().lower())
        start, end = r.get("start") or "", r.get("end") or r.get("start") or ""
        if tier is None or len(start) != 10 or len(end) != 10 or not ("2000" <= start[:4] <= str(LAST_YEAR)):
            continue
        rules = (r.get("restrictions") or "").lower()
        if "source" in (r.get("game") or "").lower() or "national" in rules or "female" in rules:
            continue
        # Female events whose page does not list the restriction still say so in their name.
        if re.search(r"qualifier|showmatch|show match|female|women|ladies|girls", (r.get("name") or "") + " " + r["page"], re.I):
            continue
        if (r.get("name") or "") in CANCELLED or OVERVIEW.match(r.get("name") or ""):
            continue
        kind = (r.get("type") or "").lower()
        row = {
            "name": r.get("name") or r["page"].replace("/", " "),
            "tier": "Major" if is_major(r) else tier,
            "start": start, "end": max(start, end),
            "prize": int(r.get("prize_pool") or 0),
            "lan": "offline" in kind,
            "country": r.get("country") or "", "city": "" if "offline" not in kind else (r.get("location") or "").split(",")[0].strip(),
            "teams": int(re.sub(r"\D", "", str(r.get("teams") or "")) or 0) or None,
            "page": r["page"],
        }
        # One event, several pages (redirects, overview pages): keep the one with most data.
        # A LAN is one event per dates, city and tier ("IEM Oakland 2016" is "Intel Extreme Masters
        # Season XI Oakland"); online events go by name.
        key = ((start, row["end"], row["city"].lower(), row["tier"]) if row["lan"] and row["city"]
               else (re.sub(r"[^a-z0-9]", "", row["name"].lower()), start))
        score = (len(r.get("placements") or []), r.get("captured") or "")
        if key not in best or score > best[key][0]:
            best[key] = (score, row)
    rows = sorted((row for _, row in best.values()), key=lambda e: (e["start"], e["name"]))
    return rows


def main() -> None:
    rows = events()
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({"source": "Liquipedia (CC-BY-SA 3.0), via the Internet Archive",
                               "last_year": LAST_YEAR, "events": rows}, ensure_ascii=False, indent=0), encoding="utf-8")
    by_year = Counter((e["start"][:4], e["tier"]) for e in rows)
    for y in sorted({e["start"][:4] for e in rows}):
        print(y, " ".join(f"{t}:{by_year[(y, t)]}" for t in ("Major", "T1", "T2", "T3", "T4")))
    print(f"{len(rows)} events -> {OUT}")
    if "--install" in sys.argv:
        shutil.copyfile(OUT, GAME_STORY / "calendar.json")
        print(f"installed {GAME_STORY / 'calendar.json'}")


if __name__ == "__main__":
    main()
