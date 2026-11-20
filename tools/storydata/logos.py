"""Team logos from archived Liquipedia pages, for the personal Story Mode build (ADR 0004).

Logos are the trademarks of their organisations. They are fetched and kept on this machine only
(tools/storydata/logos, gitignored) and copied into the game's CustomAssets folder here; nothing
is committed or published. The publishable build simply never runs `install`.

Existing logos in the game are never overwritten: your own artwork always wins.

Run:  .venv/Scripts/python logos.py fetch                     (every team page in cache/parsed)
      .venv/Scripts/python logos.py install out/2016-07-01    (the snapshot's teams -> the game)
"""
from __future__ import annotations

import csv
import io
import json
import os
import re
import shutil
import sys
from pathlib import Path

from PIL import Image

import archive

LOGOS = Path(__file__).parent / "logos"
GAME_TEAMS = Path(os.environ.get("LOCALAPPDATA", "")).parent / "LocalLow" / "NeuronaGames" / "EsportsManager" / "CustomAssets" / "Teams"
SIZE, BOX = 400, 360  # the game's team images are 400x400; keep a small transparent margin


def safe(name: str) -> str:
    return re.sub(r'[<>:"/\\|?*]', "_", name).strip()


def original_of(src: str) -> str:
    """'/commons/images/thumb/a/a1/X.png/600px-X.png' -> '/commons/images/a/a1/X.png'."""
    m = re.match(r"(/commons/images)/thumb(/.+?\.(?:png|jpg|jpeg|svg|gif))/", src, re.I)
    return m.group(1) + m.group(2) if m else src


def square(data: bytes) -> Image.Image:
    img = Image.open(io.BytesIO(data)).convert("RGBA")
    bbox = img.getbbox()  # trim transparent borders so every logo fills the box alike
    if bbox:
        img = img.crop(bbox)
    img.thumbnail((BOX, BOX), Image.LANCZOS)
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    canvas.paste(img, ((SIZE - img.width) // 2, (SIZE - img.height) // 2), img)
    return canvas


def fetch() -> None:
    LOGOS.mkdir(exist_ok=True)
    teams = [json.loads(line) for line in (archive.CACHE / "parsed" / "team.jsonl").read_text(encoding="utf-8").splitlines()]
    done = missing = 0
    for t in teams:
        target = LOGOS / f"{safe(t['name'] or t['page'])}.png"
        if target.exists() or not t.get("logo"):
            missing += not t.get("logo")
            continue
        data = None
        for src in (t["logo"], original_of(t["logo"])):  # the 600px thumb is plenty; the original is the fallback
            if src.lower().endswith(".svg"):
                continue
            try:
                data = archive.binary("https://liquipedia.net" + src)
            except archive.FetchFailed:
                data = None
            if data:
                break
        if not data:
            missing += 1
            continue
        try:
            square(data).save(target)
            done += 1
        except Exception as e:  # not an image (an archived error page)
            print(f"  {t['page']}: {e}")
            missing += 1
    print(f"logos: {done} new, {missing} without an archived logo, {len(list(LOGOS.glob('*.png')))} in {LOGOS}")


def install(snapshot: Path) -> None:
    GAME_TEAMS.mkdir(parents=True, exist_ok=True)
    with (snapshot / "Teams.csv").open(encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f, delimiter=";"))
    copied = kept = absent = 0
    for r in rows:
        source, target = LOGOS / f"{safe(r['Name'])}.png", GAME_TEAMS / f"{safe(r['Nick'])}.png"
        if target.exists():
            kept += 1
        elif source.exists():
            shutil.copyfile(source, target)
            copied += 1
        else:
            absent += 1
    print(f"installed {copied} logos into {GAME_TEAMS} ({kept} already there, kept; {absent} not available)")


if __name__ == "__main__":
    if len(sys.argv) >= 2 and sys.argv[1] == "fetch":
        fetch()
    elif len(sys.argv) >= 3 and sys.argv[1] == "install":
        install(Path(sys.argv[2]))
    else:
        print(__doc__)
