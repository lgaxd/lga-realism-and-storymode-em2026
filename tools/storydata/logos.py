"""Team logos for Story Mode start packs, from archived Liquipedia pages (ADR 0004).

Logos are the trademarks of their organisations. They are fetched and kept on this machine only
(tools/storydata/logos, gitignored) and copied into the game's CustomAssets folder here; nothing
is committed or published. The publishable build simply never runs this script.

Every team gets a logo:
  1. the logo the team used closest to the Start Date: tournament pages show each team's icon,
     and the pages are dated, so Fnatic in 2016 gets its 2015 logo, not today's;
  2. fetched from the Archive as the full-size original, else the small icon;
  3. otherwise a monogram badge in the team's colour, drawn here.
Files the mod installed are tracked (logos/installed.json): they may be replaced by a later run,
but a logo you put in the game yourself is never touched.

Run:  .venv/Scripts/python logos.py index                   (scan cached pages; quick, offline)
      .venv/Scripts/python logos.py fetch out/2016-01-01    (logos for a start pack's teams)
      .venv/Scripts/python logos.py install out/2016-01-01  (copy them into the game)
"""
from __future__ import annotations

import csv
import datetime as dt
import io
import json
import os
import re
import shutil
import sys
from collections import defaultdict
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

import archive

LOGOS = Path(__file__).parent / "logos"
INDEX = archive.CACHE / "logo_index.json"
MANIFEST = LOGOS / "installed.json"
GAME_TEAMS = Path(os.environ.get("LOCALAPPDATA", "")).parent / "LocalLow" / "NeuronaGames" / "EsportsManager" / "CustomAssets" / "Teams"
SIZE, BOX = 400, 360  # the game's team images are 400x400; keep a small transparent margin
ICON = re.compile(r'team-template-image-(?:icon|legacy)([^"]*)"[^>]*>\s*<a href="/counterstrike/([^"#]+)"[^>]*>\s*<img[^>]*?src="([^"]+)"', re.S)


def safe(name: str) -> str:
    return re.sub(r'[<>:"/\\|?*]', "_", name).strip()


def norm(title: str) -> str:
    return title.replace("_", " ").strip().lower()


def original_of(src: str) -> str:
    """'/commons/images/thumb/a/a1/X.png/60px-X.png' -> '/commons/images/a/a1/X.png'."""
    m = re.match(r"(/commons/images)/thumb(/.+?\.(?:png|jpg|jpeg|gif))/", src, re.I)
    return m.group(1) + m.group(2) if m else src


# ---------------------------------------------------------------- the index

def build_index() -> None:
    """team -> [[date, src, mode], ...] from every cached page with a date (tournaments)."""
    starts = {}
    for line in (archive.CACHE / "parsed" / "tournament.jsonl").read_text(encoding="utf-8").splitlines():
        r = json.loads(line)
        if r.get("start"):
            starts[r["page"]] = r["start"]
    teams = {}
    for line in (archive.CACHE / "parsed" / "team.jsonl").read_text(encoding="utf-8").splitlines():
        r = json.loads(line)
        if r.get("logo"):
            teams[norm(r["page"])] = r["logo"]
    index: dict[str, list] = defaultdict(list)
    for f in (archive.CACHE / "pages").glob("*/*.json"):
        raw = f.read_text(encoding="utf-8")
        if "team-template-image" not in raw:
            continue
        d = json.loads(raw)
        when = starts.get(archive.title_of(d.get("url", "")))
        if not when:
            continue
        for mode, team, src in ICON.findall(d.get("html", "")):
            mode = "dark" if "darkmode" in mode else "light" if "lightmode" in mode else "all"
            index[norm(team)].append([when, src, mode])
    for team, logo in teams.items():  # the team page's own (current) logo, undated
        index[team].append(["", logo, "dark" if "darkmode" in logo else "all"])
    INDEX.write_text(json.dumps(index), encoding="utf-8")
    print(f"logo index: {len(index)} teams")


def choose(entries: list, at: str) -> list[str]:
    """Candidate logo files for a date: the file used nearest to it, dark/neutral versions first."""
    target = dt.date.fromisoformat(at).toordinal()
    score: dict[str, float] = defaultdict(float)
    for when, src, mode in entries:
        key = original_of(src)
        try:
            gap = abs(dt.date.fromisoformat(when[:10]).toordinal() - target) if len(when) >= 10 else 4000
        except ValueError:
            gap = 4000
        score[key] += 1.0 / (1 + gap / 120) + (0.3 if mode != "light" else 0)
    srcs = {original_of(s): s for _, s, _ in entries}
    ranked = sorted(score, key=lambda k: -score[k])
    out = []
    for key in ranked[:3]:
        out += [key, srcs[key]]  # the original first, then the icon it came from
    return out


# ---------------------------------------------------------------- images

def square(data: bytes) -> Image.Image:
    img = Image.open(io.BytesIO(data)).convert("RGBA")
    bbox = img.getbbox()  # trim transparent borders so every logo fills the box alike
    if bbox:
        img = img.crop(bbox)
    scale = min(BOX / img.width, BOX / img.height)
    img = img.resize((max(1, round(img.width * scale)), max(1, round(img.height * scale))), Image.LANCZOS)
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    canvas.paste(img, ((SIZE - img.width) // 2, (SIZE - img.height) // 2), img)
    return canvas


def monogram(name: str, colour: str) -> Image.Image:
    """A badge with the team's initials, for teams the Archive has no logo of."""
    words = [w for w in re.split(r"[\s\-_.]+", re.sub(r"^(Team|The)\s+", "", name)) if w]
    letters = ("".join(w[0] for w in words[:3]) if len(words) > 1 else (words[0][:3] if words else "?")).upper()
    try:
        rgb = tuple(int(colour.lstrip("#")[i:i + 2], 16) for i in (0, 2, 4))
    except ValueError:
        rgb = (60, 70, 90)
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse((20, 20, SIZE - 20, SIZE - 20), fill=rgb + (255,), outline=(255, 255, 255, 230), width=10)
    font_path = Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts" / "arialbd.ttf"
    size = {1: 220, 2: 170, 3: 125}.get(len(letters), 110)
    font = ImageFont.truetype(str(font_path), size) if font_path.exists() else ImageFont.load_default()
    light = sum(rgb) / 3 > 150
    d.text((SIZE / 2, SIZE / 2 + 4), letters, font=font, anchor="mm", fill=(20, 20, 20, 255) if light else (255, 255, 255, 255))
    return img


def fetch(pack: Path) -> None:
    if not INDEX.exists():
        build_index()
    index = json.loads(INDEX.read_text(encoding="utf-8"))
    at = pack.name if re.match(r"\d{4}-\d{2}-\d{2}$", pack.name) else dt.date.today().isoformat()
    folder = LOGOS / at
    folder.mkdir(parents=True, exist_ok=True)
    with (pack / "Teams.csv").open(encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f, delimiter=";"))
    real = drawn = kept = 0
    for i, r in enumerate(rows):
        target = folder / f"{safe(r['Name'])}.png"
        if target.exists():
            kept += 1
            continue
        image = None
        for src in choose(index.get(norm(r["Name"]), []), at):
            if src.lower().endswith(".svg"):
                continue
            try:
                data = archive.binary("https://liquipedia.net" + src)
                image = square(data) if data else None
            except Exception:
                image = None
            if image is not None:
                break
        if image is None:
            image = monogram(r["Name"], r.get("BgColor") or "")
            drawn += 1
        else:
            real += 1
        image.save(target)
        if (i + 1) % 25 == 0:
            print(f"  {i + 1}/{len(rows)}: {real} logos, {drawn} monograms", flush=True)
    print(f"{at}: {real} archived logos, {drawn} monograms, {kept} already done ({len(rows)} teams)")


def install(pack: Path) -> None:
    at = pack.name
    folder = LOGOS / at
    GAME_TEAMS.mkdir(parents=True, exist_ok=True)
    mine = set(json.loads(MANIFEST.read_text(encoding="utf-8"))) if MANIFEST.exists() else set()
    with (pack / "Teams.csv").open(encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f, delimiter=";"))
    copied = kept = absent = 0
    for r in rows:
        name = f"{safe(r['Nick'])}.png"
        source, target = folder / f"{safe(r['Name'])}.png", GAME_TEAMS / name
        if not source.exists():
            absent += 1
        elif target.exists() and name not in mine:
            kept += 1  # yours: never replaced
        else:
            shutil.copyfile(source, target)
            mine.add(name)
            copied += 1
    MANIFEST.write_text(json.dumps(sorted(mine)), encoding="utf-8")
    print(f"installed {copied} logos into {GAME_TEAMS} ({kept} of yours kept; {absent} missing - run fetch)")


if __name__ == "__main__":
    if len(sys.argv) >= 2 and sys.argv[1] == "index":
        build_index()
    elif len(sys.argv) >= 3 and sys.argv[1] in ("fetch", "install"):
        (fetch if sys.argv[1] == "fetch" else install)(Path(sys.argv[2]))
    else:
        print(__doc__)
