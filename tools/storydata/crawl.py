"""Step 2: crawl archived Liquipedia pages, following the data (ADR 0004).

Seeds are the tournament lists by tier. Tournaments lead to the teams that played them (prize
tables, teamcards); teams lead to their players (rosters); players lead to their other teams
(results). Pages are fetched through the Internet Archive (archive.page, cached), parsed
(parse.parse) and appended to cache/records/<kind>.jsonl. The queue is saved to disk, so the
crawl can be stopped (Ctrl+C) and resumed at any time.

Run:  .venv/Scripts/python crawl.py [--max N]
"""
from __future__ import annotations

import argparse
import heapq
import json
import re
import time
import urllib.parse

from bs4 import BeautifulSoup

import archive
import parse

RECORDS = archive.CACHE / "records"
STATE = archive.CACHE / "crawl.state.json"
BASE = "https://liquipedia.net/counterstrike/"

# Tier list pages: their links are tournaments. (Lower numbers are crawled first.)
SEEDS = {
    "S-Tier_Tournaments": 0, "A-Tier_Tournaments": 0, "B-Tier_Tournaments": 1, "C-Tier_Tournaments": 2,
    "Premier_Tournaments": 0, "Major_Tournaments": 0, "Minor_Tournaments": 1,
}
PRIORITY = {"tournament": 1, "team": 2, "player": 3, None: 4}

# Links that are never data pages.
SKIP = re.compile(r"^(Special|File|Template|Category|User|Liquipedia|Help|Talk|Portal|Main Page)[: ]|/(Results|Matches|Statistics|Played Matches)$", re.I)


def page_url(title: str) -> str:
    return BASE + urllib.parse.quote(title.replace(" ", "_"), safe="/()'.,-!&")


def load_state() -> dict:
    if STATE.exists():
        return json.loads(STATE.read_text(encoding="utf-8"))
    return {"queue": [[PRIORITY[None] - 4 + p, 0, t, "seed"] for t, p in SEEDS.items()], "seen": list(SEEDS), "done": 0, "counter": 0}


def save_state(state: dict) -> None:
    tmp = STATE.with_suffix(".tmp")
    tmp.write_text(json.dumps(state), encoding="utf-8")
    tmp.replace(STATE)


def links_of(html: str) -> list[str]:
    soup = BeautifulSoup(html, "lxml")
    content = soup.select_one("#mw-content-text") or soup
    titles = []
    for a in content.select("a[href*='/counterstrike/']"):
        t = parse.link_title(a)
        if t and not SKIP.search(t):
            titles.append(t)
    return titles


def next_links(record: dict | None, html: str, source_kind: str) -> list[tuple[str, str]]:
    """(title, expected kind) pairs to queue after a page."""
    if source_kind == "seed":
        return [(t, "tournament") for t in links_of(html)]
    if record is None:
        return []
    out: list[tuple[str, str]] = []
    if record["kind"] == "tournament":
        out += [(p["team"], "team") for p in record["placements"] if p.get("team")]
        for r in record["rosters"]:
            if r.get("team"):
                out.append((r["team"], "team"))
            out += [(p["page"], "player") for p in r["players"] if p.get("page")]
    elif record["kind"] == "team":
        out += [(m["page"], "player") for m in record["members"] + record["staff"] if m.get("page")]
    elif record["kind"] == "player":
        out += [(r["team"], "team") for r in record["results"] if r.get("team")]
        out += [(r["tournament"].split("#")[0], "tournament") for r in record["results"] if r.get("tournament")]
    return out


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--max", type=int, default=0, help="stop after N pages (0 = no limit)")
    args = ap.parse_args()

    RECORDS.mkdir(parents=True, exist_ok=True)
    state = load_state()
    queue = [tuple(x) for x in state["queue"]]
    heapq.heapify(queue)
    seen = set(state["seen"])

    # Everything the Archive holds goes in at the lowest priority, so pages the link graph
    # never reaches (e.g. tournaments after the archived tier lists stop, in 2019) are still
    # collected once the data-driven queue is exhausted.
    urls = archive.CACHE / "urls.jsonl"
    # Exact latest-capture timestamps (enumerate_pages.py) make each fetch a single cheap request.
    known = {}
    if urls.exists():
        for line in urls.read_text(encoding="utf-8").splitlines():
            r = json.loads(line)
            known[r["title"]] = (r["url"], r["last"])
    if urls.exists() and not state.get("seeded_all"):
        added = 0
        for line in urls.read_text(encoding="utf-8").splitlines():
            t = json.loads(line)["title"]
            if t not in seen and not SKIP.search(t):
                seen.add(t)
                state["counter"] += 1
                heapq.heappush(queue, (9, state["counter"], t, "any"))
                added += 1
        state["seeded_all"] = True
        print(f"queued {added} enumerated pages at the lowest priority", flush=True)
    outputs = {k: (RECORDS / f"{k}.jsonl").open("a", encoding="utf-8") for k in ("tournament", "team", "player")}
    fetched = 0
    started = time.time()
    try:
        while queue and (not args.max or fetched < args.max):
            prio, _, title, expected = heapq.heappop(queue)
            try:
                url, stamp = known.get(title, (None, None))
                p = archive.page(page_url(title), stamp=stamp, captured_url=url)
            except archive.FetchFailed as e:
                # Never let one page stop the crawl: retry it later, give up after 3 failures.
                failures = state.setdefault("failures", {})
                failures[title] = failures.get(title, 0) + 1
                if failures[title] < 3:
                    state["counter"] += 1
                    heapq.heappush(queue, (prio + 10, state["counter"], title, expected))
                print(f"  fetch failed ({failures[title]}x): {e}", flush=True)
                continue
            except Exception as e:
                print(f"  unexpected error on {title}: {e}", flush=True)
                continue
            fetched += 1
            state["done"] += 1
            if p is None:
                continue
            record = None
            if expected != "seed":  # "any" pages are parsed as whatever they turn out to be
                try:
                    record = parse.parse(p["html"])
                except Exception as e:  # a malformed page must not stop the crawl
                    print(f"  parse error on {title}: {e}", flush=True)
                if record is not None:
                    record["page"] = title
                    record["captured"] = p["captured"]
                    outputs[record["kind"]].write(json.dumps(record, ensure_ascii=False) + "\n")
            for t, kind in next_links(record, p["html"], expected):
                if t not in seen:
                    seen.add(t)
                    state["counter"] += 1
                    heapq.heappush(queue, (PRIORITY.get(kind, 4), state["counter"], t, kind))
            if fetched % 25 == 0:
                for f in outputs.values():
                    f.flush()
                state["queue"] = [list(x) for x in queue]
                state["seen"] = list(seen)
                save_state(state)
                rate = fetched / max(1, time.time() - started)
                print(f"{state['done']} pages done, {len(queue)} queued ({rate:.2f}/s)", flush=True)
    finally:
        for f in outputs.values():
            f.close()
        state["queue"] = [list(x) for x in queue]
        state["seen"] = list(seen)
        save_state(state)
        print(f"stopped: {state['done']} pages done, {len(queue)} queued", flush=True)


if __name__ == "__main__":
    main()
