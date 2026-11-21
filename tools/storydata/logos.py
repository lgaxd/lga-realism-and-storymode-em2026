"""Team logos for Story Mode start packs, from archived Liquipedia pages (ADR 0004).

Logos are the trademarks of their organisations. They are fetched and kept on this machine only
(tools/storydata/logos, gitignored) and put in the game's start packs here; nothing is committed
or published. The publishable build simply never runs this script.

Every team gets a logo:
  1. the logo the team used closest to the Start Date: tournament pages show each team's icon,
     and the pages are dated, so Fnatic in 2016 gets its 2015 logo, not today's;
  2. fetched from the Archive as the full-size original, else the small icon;
  3. otherwise a monogram badge in the team's colour, drawn here.
Each start pack carries its own logos (story/<date>/logos/<team name>.png) and photos
(story/<date>/photos/<player nick>.png, the infobox photo cropped to the game's 400x417 portrait
with soft edges). The mod answers the game's image lookups from the pack of the world being played;
nothing is copied into CustomAssets, and an image you put there yourself is never touched.

A career keeps the teams of the pack build it began from. A team a rebuilt pack no longer has
("Team SoloMid" before the data said its players had left) can be kept in the pack's logos with
`legacy`: listed in logos/<date>/legacy.tsv, installed with the rest.

Run:  .venv/Scripts/python logos.py index                   (scan cached pages; quick, offline)
      .venv/Scripts/python logos.py fetch out/2016-01-01    (logos for a start pack's teams)
      .venv/Scripts/python logos.py install out/2016-01-01  (put them in the game's start pack)
      .venv/Scripts/python logos.py photos out/2016-01-01   (player photos: fetch and put in the pack)
      .venv/Scripts/python logos.py legacy out/2016-01-01 "Team SoloMid=TSM" "Games Academy"
                                                          (keep logos of teams older careers have:
                                                           name as the career shows it[=wiki page])
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
GAME_STORY = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Esports Manager 2026\BepInEx\plugins\RealismOverhaul\story")
MANIFEST = GAME_STORY / "logos-installed.json"
PHOTO_MANIFEST = GAME_STORY / "photos-installed.json"
GAME_PLAYERS = Path(os.environ.get("LOCALAPPDATA", "")).parent / "LocalLow" / "NeuronaGames" / "EsportsManager" / "CustomAssets" / "Players"
PHOTO_W, PHOTO_H = 400, 417  # the game's player portraits
OLD_MANIFEST = LOGOS / "installed.json"  # before packs carried their logos
SIZE, BOX = 400, 360  # the game's team images are 400x400; keep a small transparent margin
ICON = re.compile(r'team-template-image-(?:icon|legacy)([^"]*)"[^>]*>\s*<a href="/counterstrike/([^"#]+)"[^>]*>\s*<img[^>]*?src="([^"]+)"', re.S)


def safe(name: str) -> str:
    return re.sub(r'[<>:"/\\|?*]', "_", name).strip()


def teams_of(pack: Path) -> list[tuple[str, str, dict]]:
    """(nick, wiki page, row) for each team of a start pack."""
    pages = {}
    tsv = pack / "team_pages.tsv"
    if tsv.exists():
        for line in tsv.read_text(encoding="utf-8").splitlines():
            nick, _, page = line.partition("\t")
            pages[nick] = page
    with (pack / "Teams.csv").open(encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f, delimiter=";"))
    return [(r["Nick"], pages.get(r["Nick"], r["Name"]), r) for r in rows]


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
    rows = teams_of(pack)
    real = drawn = kept = 0
    for i, (_, page, r) in enumerate(rows):
        target = folder / f"{safe(page)}.png"
        if target.exists():
            kept += 1
            continue
        image, unsure = None, False
        for src in choose(index.get(norm(page), []), at):
            if src.lower().endswith(".svg"):
                continue
            try:
                data = archive.binary("https://liquipedia.net" + src)
                image = square(data) if data else None
            except archive.FetchFailed:
                unsure = True  # throttled: try this team again on the next run
                break
            except Exception:
                image = None
            if image is not None:
                break
        if image is None and unsure:
            continue
        if image is None:
            image = monogram(r["Name"], r.get("BgColor") or "")  # the shown name, without brackets
            drawn += 1
        else:
            real += 1
        image.save(target)
        if (i + 1) % 25 == 0:
            print(f"  {i + 1}/{len(rows)}: {real} logos, {drawn} monograms", flush=True)
    print(f"{at}: {real} archived logos, {drawn} monograms, {kept} already done ({len(rows)} teams)")


def legacy_of(at: str) -> list[tuple[str, str]]:
    path = LOGOS / at / "legacy.tsv"
    if not path.exists():
        return []
    return [tuple(line.split("\t", 1)) for line in path.read_text(encoding="utf-8").splitlines() if "\t" in line]


def logo_for(page: str, at: str, index: dict) -> Image.Image | None:
    """The archived logo of a team page nearest to `at`; None if there is none (or throttled)."""
    for src in choose(index.get(norm(page), []), at):
        if src.lower().endswith(".svg"):
            continue
        try:
            data = archive.binary("https://liquipedia.net" + src)
            image = square(data) if data else None
        except archive.FetchFailed:
            return None
        except Exception:
            image = None
        if image is not None:
            return image
    return None


def legacy(pack: Path, entries: list[str]) -> None:
    """Keep logos for teams a career from an older build of this pack still has."""
    at = pack.name
    if not INDEX.exists():
        build_index()
    index = json.loads(INDEX.read_text(encoding="utf-8"))
    known = dict(legacy_of(at))
    for entry in entries:
        name, _, page = entry.partition("=")
        name, page = name.strip(), (page or name).strip()
        target = LOGOS / at / f"{safe(page)}.png"
        if not target.exists():
            image = logo_for(page, at, index)
            if image is None:
                print(f"  {name}: no archived logo for page '{page}' (or throttled: run again)")
                continue
            image.save(target)
        known[name] = page
        print(f"  {name}: logo of '{page}'")
    (LOGOS / at / "legacy.tsv").write_text("".join(f"{n}\t{p}\n" for n, p in sorted(known.items())), encoding="utf-8")
    install(pack)


def install(pack: Path) -> None:
    at = pack.name
    folder, target_dir = LOGOS / at, GAME_STORY / at / "logos"
    if target_dir.exists():
        shutil.rmtree(target_dir)  # the pack's own copy: rebuilt whole
    target_dir.mkdir(parents=True)
    copied = absent = 0
    teams = teams_of(pack)
    # The game asks for a team's image by its name ("Team EnVyUs"); a file under the nick helps
    # careers begun from an older pack, unless another team has that name (CLG Red's nick is not
    # CLG's name).
    names = {safe(row["Name"]).lower() for _, _, row in teams}
    for nick, page, row in teams:
        source = folder / f"{safe(page)}.png"
        if not source.exists():
            absent += 1
            continue
        shutil.copyfile(source, target_dir / f"{safe(row['Name'])}.png")
        if safe(nick).lower() not in names:
            shutil.copyfile(source, target_dir / f"{safe(nick)}.png")
        copied += 1
    for name, page in legacy_of(at):
        source = folder / f"{safe(page)}.png"
        target = target_dir / f"{safe(name)}.png"
        if source.exists() and not target.exists():
            shutil.copyfile(source, target)
            copied += 1
    print(f"{at}: {copied} logos put in {target_dir}; the mod installs them when this world is loaded ({absent} missing - run fetch)")


def portrait(data: bytes) -> Image.Image:
    """Cover-crop a photo to the game's portrait frame, keeping the head (upper part) in view."""
    img = Image.open(io.BytesIO(data)).convert("RGBA")
    scale = max(PHOTO_W / img.width, PHOTO_H / img.height)
    img = img.resize((max(PHOTO_W, round(img.width * scale)), max(PHOTO_H, round(img.height * scale))), Image.LANCZOS)
    left = (img.width - PHOTO_W) // 2
    top = min(img.height - PHOTO_H, int((img.height - PHOTO_H) * 0.15))
    return img.crop((left, top, left + PHOTO_W, top + PHOTO_H))


def faded(img: Image.Image) -> Image.Image:
    """A photo with soft transparent edges, like the game's cut-out portraits: the flag and the
    frame drawn around a portrait stay visible."""
    from PIL import ImageChops, ImageFilter
    img = img.convert("RGBA")
    mask = Image.new("L", img.size, 0)
    w, h = img.size
    ImageDraw.Draw(mask).ellipse((int(w * 0.10), int(h * 0.02), int(w * 0.90), int(h * 1.25)), fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(18))
    img.putalpha(ImageChops.multiply(img.getchannel("A"), mask))
    return img


def photos(pack: Path) -> None:
    """Fetch the wiki photo of every player of a start pack and put them in the game's pack."""
    from snapshot import title
    src_of = {}
    for line in (archive.CACHE / "parsed" / "player.jsonl").read_text(encoding="utf-8").splitlines():
        r = json.loads(line)
        if r.get("photo") and "NoImage" not in r["photo"]:  # the wiki's silhouette is no photo
            src_of[title(r["page"])] = r["photo"]
            src_of.setdefault(title(r.get("id") or r["page"]), r["photo"])
    pages = {}
    tsv = pack / "player_pages.tsv"
    for line in tsv.read_text(encoding="utf-8").splitlines():
        nick, _, page = line.partition("\t")
        pages[nick] = page
    mine = set(json.loads(PHOTO_MANIFEST.read_text(encoding="utf-8"))) if PHOTO_MANIFEST.exists() else set()
    folder, target_dir = LOGOS / "players", GAME_STORY / pack.name / "photos"
    folder.mkdir(parents=True, exist_ok=True)
    if target_dir.exists():
        shutil.rmtree(target_dir)
    target_dir.mkdir(parents=True)
    got = fetched = yours = none = 0
    for i, (nick, page) in enumerate(pages.items()):
        name = f"{safe(nick)}.png"
        if (GAME_PLAYERS / name).exists() and name not in mine:
            yours += 1  # the player's own photo stays; nothing to fetch
            continue
        cached = folder / f"{safe(page)}.png"
        if not cached.exists():
            src = src_of.get(title(page))
            if not src or src.lower().endswith(".svg"):
                none += 1
                continue
            image = None
            for url in (original_of(src), src):
                try:
                    data = archive.binary("https://liquipedia.net" + url)
                    image = portrait(data) if data else None
                except archive.FetchFailed:
                    break  # throttled: next run
                except Exception:
                    image = None
                if image is not None:
                    break
            if image is None:
                none += 1
                continue
            image.save(cached)
            fetched += 1
        faded(Image.open(cached)).save(target_dir / name)
        got += 1
        if (i + 1) % 100 == 0:
            print(f"  {i + 1}/{len(pages)}: {got} photos ({fetched} fetched)", flush=True)
    print(f"{pack.name}: {got} photos put in {target_dir} ({fetched} fetched now), {yours} of yours kept, "
          f"{none} without a wiki photo yet; {len(pages)} players with a wiki page")


if __name__ == "__main__":
    if len(sys.argv) >= 2 and sys.argv[1] == "index":
        build_index()
    elif len(sys.argv) >= 3 and sys.argv[1] == "photos":
        photos(Path(sys.argv[2]))
    elif len(sys.argv) >= 4 and sys.argv[1] == "legacy":
        legacy(Path(sys.argv[2]), sys.argv[3:])
    elif len(sys.argv) >= 3 and sys.argv[1] in ("fetch", "install"):
        (fetch if sys.argv[1] == "fetch" else install)(Path(sys.argv[2]))
    else:
        print(__doc__)
