"""Step 3: build "the world on date D" in the game's database format (Story Mode start).

Reads the parsed archive records (reparse.py) and a reference database exported from the game
(EmdbExport), and writes Players.csv, Teams.csv, Staff.csv, roster_order.json (plus the reference
Tournaments.csv and Sponsors.csv, unchanged for now) and a report into out/<date>/.

How the world is decided:
  * Rosters: a player is on a team at D if a team page lists him with join <= D < leave; without
    that, his latest tournament roster or result in the 180 days before D decides.
  * Teams: every team with at least 4 known players at D (filled up to 5 with generated players).
  * Strength: placements weighted by tier and place, fading over the year before D (results in
    the 180 days after D count at half weight: they show the strength the roster already had).
    Team and player scores are mapped by quantile onto the reference database's Rating / Skill
    distribution, so every era plays on the game's familiar scale.
  * Attributes: a player in the reference database keeps his own profile, shifted to his skill at
    D; anyone else borrows the profile of a reference player with the same skill and role.

The output mixes CC-BY-SA facts with numbers from the reference database: it is built locally
and never published (ADR 0004).

Run:  .venv/Scripts/python snapshot.py 2013-08-01 [--reference DIR] [--out DIR]
      .venv/Scripts/python snapshot.py 2026-04-01 --calibrate   (compare with the reference)
"""
from __future__ import annotations

import argparse
import bisect
import csv
import datetime as dt
import hashlib
import json
import re
from collections import Counter, defaultdict
from pathlib import Path

import archive

PARSED = archive.CACHE / "parsed"
GAME = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Esports Manager 2026\BepInEx")
DEFAULT_REFERENCE = GAME / "RealismOverhaul-emdb-luccaneta_s_GLOBAL_UPDATED"
OUT = Path(__file__).parent / "out"

TIER_WEIGHT = {"s": 1.0, "premier": 1.0, "major": 0.55, "a": 0.55, "b": 0.3, "minor": 0.3, "c": 0.15,
               "qualifier": 0.08, "monthly": 0.05, "weekly": 0.03}
# Attributes that measure playing ability (shifted with skill); the rest is personality/physique.
ABILITY = ["AWP", "Rifle", "Pistol", "Reaction", "Tactic", "Creativity", "Teamwork", "Grenades",
           "Clutch", "Perception"]
ROLES = {"awp": "AWPer", "sniper": "AWPer", "rifl": "Rifler", "entry": "Entry fragger", "lurk": "Lurker",
         "support": "Support", "in-game leader": "In-game leader", "igl": "In-game leader", "captain": "In-game leader"}
NOT_A_TEAM = {"TBD", "TBA", ""}


# ---------------------------------------------------------------- small helpers

def day(text: str | None) -> int | None:
    """'YYYY[-MM[-DD]]' -> ordinal day; unknown parts become the middle of the period."""
    if not text:
        return None
    m = re.match(r"(\d{4})(?:-(\d{2}))?(?:-(\d{2}))?", text)
    if not m:
        return None
    y, mo, d = int(m.group(1)), m.group(2), m.group(3)
    try:
        return dt.date(y, int(mo) if mo else 7, int(d) if d else (15 if mo else 1)).toordinal()
    except ValueError:
        return None


def place_of(text: str | None) -> int | None:
    m = re.search(r"\d+", text or "")
    return int(m.group()) if m else None


def place_weight(place: int | None) -> float:
    if place is None:
        return 0.05
    for limit, w in ((1, 1.0), (2, 0.7), (4, 0.5), (8, 0.32), (16, 0.18)):
        if place <= limit:
            return w
    return 0.08


def tier_weight(tier: str | None) -> float:
    t = (tier or "").lower().replace("-tier", "").replace(" tier", "").strip()
    return TIER_WEIGHT.get(t, 0.05)


def fade(when: int, at: int) -> float:
    """Weight of a result dated `when` for the world at `at`."""
    if when <= at:
        return max(0.0, 1 - (at - when) / 365)
    return max(0.0, 0.5 * (1 - (when - at) / 180))


def stable(*parts: str) -> int:
    """Deterministic pseudo-random number from text (same input, same world)."""
    return int(hashlib.sha1("|".join(parts).encode("utf-8")).hexdigest()[:8], 16)


def clean_name(text: str | None) -> str:
    """'(Native script) Romanized Name' -> 'Romanized Name'."""
    t = re.sub(r"^\(.*?\)\s*", "", text or "").strip()
    return t.replace(";", ",")


def short_team(title: str) -> str:
    t = re.sub(r"^Team ", "", title)
    t = re.sub(r" (Esports|eSports|e-Sports|Gaming|Clan|Esport|Club)$", "", t)
    return t.replace(";", ",")[:20]


def quantile_map(values: dict, target: list[int]) -> dict:
    """Map scores onto a target distribution by rank; equal scores get the same value."""
    if not values or not target:
        return {}
    tgt = sorted(target)
    ordered = sorted(set(values.values()))
    keys_by_score = defaultdict(list)
    for k, v in values.items():
        keys_by_score[v].append(k)
    n, below, out = len(values), 0, {}
    for v in ordered:
        group = keys_by_score[v]
        q = (below + len(group) / 2) / n
        out.update({k: tgt[min(len(tgt) - 1, int(q * len(tgt)))] for k in group})
        below += len(group)
    return out


def read_table(path: Path) -> tuple[list[str], list[dict]]:
    with path.open(encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f, delimiter=";")
        return list(reader.fieldnames or []), list(reader)


def write_table(path: Path, header: list[str], rows: list[dict]) -> None:
    with path.open("w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, delimiter=";", lineterminator="\n")
        w.writerow(header)
        for r in rows:
            w.writerow([str(r.get(h, "") if r.get(h) is not None else "").replace(";", ",").replace("\n", " ")
                        for h in header])


def roles_from(text: str | None) -> list[str]:
    out = []
    for part in re.split(r"[,/]| and ", (text or "").lower()):
        for key, role in ROLES.items():
            if key in part and role not in out:
                out.append(role)
    return out


# ---------------------------------------------------------------- the archive, indexed

class Archive:
    def __init__(self) -> None:
        self.tournaments: dict[str, dict] = {}
        self.teams: dict[str, dict] = {}
        self.players: dict[str, dict] = {}
        for kind, store in (("tournament", self.tournaments), ("team", self.teams), ("player", self.players)):
            path = PARSED / f"{kind}.jsonl"
            if not path.exists():
                continue
            for line in path.read_text(encoding="utf-8").splitlines():
                r = json.loads(line)
                old = store.get(r["page"])
                if old is None or r.get("captured", "") > old.get("captured", ""):
                    store[r["page"]] = r
        # One entity can be stored under several titles (redirects): the page heading is canonical.
        self.alias: dict[str, str] = {}
        for store, field in ((self.teams, "name"), (self.players, "id")):
            for page, r in list(store.items()):
                canon = r.get(field) or page
                canon = canon[:1].upper() + canon[1:]
                self.alias[page[:1].upper() + page[1:]] = canon
                if canon != page:
                    keep = store.get(canon)
                    if keep is None or r.get("captured", "") > keep.get("captured", ""):
                        store[canon] = r
                    del store[page]

        self.info: dict[str, dict] = defaultdict(dict)          # player -> nick, country, name
        self.spells: dict[str, list] = defaultdict(list)        # player -> (team, join, bench, leave)
        self.listed: dict[str, set] = defaultdict(set)          # team -> players its page lists
        self.evidence: dict[str, list] = defaultdict(list)      # player -> (day, team)
        self.results: dict[str, dict] = defaultdict(dict)       # team -> tournament -> (day, place, tier, prize)
        self.played: dict[str, dict] = defaultdict(dict)        # player -> tournament -> (day, team)
        self.staff: dict[str, list] = defaultdict(list)         # team -> staff spells
        self._index()

    def _is_team(self, key: str) -> bool:
        """A roster entry that is really a team page (an icon link), not a player."""
        return key in self.teams or key in self.results or key in self.alias.values() and key in self.teams

    def team_key(self, title: str | None) -> str | None:
        if not title or title in NOT_A_TEAM:
            return None
        title = title[:1].upper() + title[1:]  # MediaWiki titles ignore the first letter's case
        return self.alias.get(title, title)

    def player_key(self, page: str | None, nick: str | None) -> str | None:
        if page:
            page = page[:1].upper() + page[1:]
            return self.alias.get(page, page)
        return f"id:{nick.lower()}" if nick else None

    def _note(self, key: str, nick: str | None, country: str | None = None, name: str | None = None) -> None:
        i = self.info[key]
        if nick:
            i.setdefault("nick", nick)
        if country and country != "Non-representing":
            i.setdefault("country", country)
        if name:
            i.setdefault("name", clean_name(name))

    def _index(self) -> None:
        # The Main Circuit is 1.x/1.6 -> CS:GO -> CS2: the parallel CS:Source scene does not count.
        self.off_circuit = {k for k, t in self.tournaments.items() if "source" in (t.get("game") or "").lower()}
        for tkey, t in self.tournaments.items():
            if tkey in self.off_circuit:
                continue
            when = day(t.get("end")) or day(t.get("start"))
            if when is None:
                continue
            for p in t["placements"]:
                team = self.team_key(p.get("team"))
                if team:
                    self.results[team].setdefault(tkey, (when, place_of(p.get("place")), t.get("tier"), p.get("prize")))
            for card in t["rosters"]:
                team = self.team_key(card.get("team"))
                for p in card["players"]:
                    key = self.player_key(p.get("page"), p.get("id"))
                    if not key or not team or not p.get("id") or self._is_team(key):
                        continue
                    self._note(key, p.get("id"), p.get("country"))
                    self.evidence[key].append((day(t.get("start")) or when, team))
                    self.played[key].setdefault(tkey, (when, team))
        for team, r in self.teams.items():
            for m in r["members"]:
                # Staff can sit in the same tables (Organization section): players have no position,
                # or one that is still a playing role.
                pos = m.get("position") or ""
                if pos and not re.search(r"substitute|stand-in|loan|player|captain|leader|awp|rifler", pos, re.I):
                    self.staff[team].append(m)
                    continue
                key = self.player_key(m.get("page"), m.get("id"))
                if not key:
                    continue
                self._note(key, m.get("id"), m.get("country"), m.get("name"))
                self.listed[team].add(key)
                self.spells[key].append((team, day(m.get("join")), day(m.get("inactive")), day(m.get("leave"))))
            for s in r["staff"]:
                self.staff[team].append(s)
            for res in r.get("results", []):
                when = day(res.get("date"))
                if when and res.get("tournament") and res["tournament"] not in self.off_circuit:
                    self.results[team].setdefault(res["tournament"], (when, place_of(res.get("place")), res.get("tier"), res.get("prize")))
        for key, r in self.players.items():
            self._note(key, r.get("id"), r.get("nationality"), r.get("romanized") or r.get("name"))
            for h in r.get("history", []):  # the infobox team history: dated spells
                team = self.team_key(h.get("team"))
                if team:
                    self.spells[key].append((team, day(h.get("join")), None, day(h.get("leave"))))
            for res in r["results"]:
                team, when = self.team_key(res.get("team")), day(res.get("date"))
                if team and when and res.get("tournament") and res["tournament"] not in self.off_circuit:
                    self.evidence[key].append((when, team))
                    self.played[key].setdefault(res["tournament"], (when, team))
                    self.results[team].setdefault(res["tournament"], (when, place_of(res.get("place")), res.get("tier"), res.get("prize")))

    # -- the world at a date

    def team_at(self, player: str, at: int) -> tuple[str, bool] | None:
        """(team, benched) for a player at day `at`, or None (see also from_page)."""
        best = None
        for team, join, bench, leave in self.spells.get(player, []):
            if join is not None and join <= at and (leave is None or leave > at):
                if best is None or join > best[1]:
                    best = (team, join, bench is not None and bench <= at)
        recent = [(d, t) for d, t in self.evidence.get(player, []) if at - 180 <= d <= at]
        latest = max(recent) if recent else None
        if best is not None:
            # A stale open spell loses to newer evidence for another team.
            if latest and latest[1] != best[0] and latest[0] > best[1]:
                return self._unless_contradicted(player, latest[1], at)
            return best[0], best[2]
        return self._unless_contradicted(player, latest[1], at) if latest else None

    def _unless_contradicted(self, player: str, team: str, at: int) -> tuple[str, bool] | None:
        # The team's own page has the final word on who it had and when.
        if player in self.listed.get(team, ()):
            for t, join, _, leave in self.spells[player]:
                if t == team and join is not None and join <= at and (leave is None or leave > at):
                    return team, False
            return None
        return team, False

    def team_score(self, team: str, at: int) -> float:
        return sum(tier_weight(tier) * place_weight(place) * fade(when, at)
                   for when, place, tier, _ in self.results.get(team, {}).values())

    def player_score(self, player: str, at: int) -> float:
        total = 0.0
        for tour, (when, team) in self.played.get(player, {}).items():
            res = self.results.get(team, {}).get(tour)
            tier = res[2] if res else (self.tournaments.get(tour) or {}).get("tier")
            total += tier_weight(tier) * place_weight(res[1] if res else None) * fade(when, at)
        return total

    def earnings(self, team: str, at: int) -> int:
        return sum(prize or 0 for when, _, _, prize in self.results.get(team, {}).values() if when <= at)

    def last_seen(self, player: str) -> int | None:
        days = [d for d, _ in self.evidence.get(player, [])]
        days += [x for _, j, b, l in self.spells.get(player, []) for x in (j, b, l) if x]
        return max(days) if days else None


# ---------------------------------------------------------------- the reference database

class Reference:
    def __init__(self, folder: Path) -> None:
        self.folder = folder
        self.player_header, self.players = read_table(folder / "Players.csv")
        self.team_header, self.teams = read_table(folder / "Teams.csv")
        self.staff_header, self.staff = read_table(folder / "Staff.csv")
        self.by_page, nicks = {}, defaultdict(list)
        for r in self.players:
            if r["Liquipedia"]:
                self.by_page[archive.title_of(r["Liquipedia"])] = r
            nicks[r["Nick"].lower()].append(r)
        self.by_nick = {k: v[0] for k, v in nicks.items() if len(v) == 1}
        self.team_nick = {}
        for t in self.teams:
            for name in (t["Name"], t["Nick"], "Team " + t["Nick"]):
                self.team_nick.setdefault(name.lower(), t["Nick"])
        on_team = [r for r in self.players if r["Team"] and not r["Retired"] and r["Skill"].isdigit()]
        self.skills = [int(r["Skill"]) for r in on_team]
        live = [t for t in self.teams if not t["Disbanded"] and (t["Rating"] or "0") != "0"]
        self.ratings = [int(t["Rating"]) for t in live]
        self.ers = [int(t["ERS"]) for t in live if t["ERS"].isdigit()]
        # Donors: reference players by (skill, primary role), for profiles of unknown players.
        self.donors: dict[tuple, list] = defaultdict(list)
        for r in on_team:
            self.donors[(int(r["Skill"]), r["Role1"] or "Rifler")].append(r)
        self.coaches = [s for s in self.staff if s["Role"] == "Coach"]

    def find(self, page: str | None, nick: str) -> dict | None:
        return self.by_page.get(page or "") or self.by_nick.get(nick.lower())

    def donor(self, skill: int, role: str, seed: str) -> dict:
        for spread in range(0, 20):
            pool = [d for s in (skill - spread, skill + spread) for d in self.donors.get((s, role), [])]
            if pool:
                return pool[stable(seed) % len(pool)]
        return self.players[stable(seed) % len(self.players)]


# ---------------------------------------------------------------- building

def build(at_text: str, ref: Reference, arc: Archive) -> dict:
    at = day(at_text)
    at_date = dt.date.fromordinal(at)

    # Rosters at D.
    roster: dict[str, list] = defaultdict(list)
    for player in set(arc.spells) | set(arc.evidence):
        died = day((arc.players.get(player) or {}).get("died"))
        if died is not None and died <= at:
            continue
        hit = arc.team_at(player, at)
        if hit:
            roster[hit[0]].append((player, hit[1]))
    # A team's own page decides its roster: tournament appearances (redirected names, stand-ins)
    # only fill a team whose page gives fewer than five players on that date.
    for team, members in roster.items():
        listed = [m for m in members if m[0] in arc.listed.get(team, ())]
        if len(listed) >= 5 and len(listed) < len(members):
            members[:] = listed
    # The same player can hide under two keys (a redirect we never fetched: "NBK" / "NBK-"):
    # teammates whose nicks match ignoring case and punctuation are one player.
    def norm(text: str) -> str:
        return re.sub(r"[^a-z0-9]", "", text.lower())
    # Alternate IDs on player pages ("pashaBiceps" was also "pasha") name the same player.
    alt: dict[str, str] = {}
    for key, rec in arc.players.items():
        for other in re.split(r"[,/;]", rec.get("alternate_ids") or ""):
            if norm(other):
                alt.setdefault(norm(other), norm(rec.get("id") or key))
    for team, members in roster.items():
        seen_nick: dict[str, tuple] = {}
        for player, benched in list(members):
            nick = norm(arc.info.get(player, {}).get("nick") or player.removeprefix("id:"))
            nick = alt.get(nick, nick)
            other = seen_nick.get(nick)
            if other is None:
                seen_nick[nick] = (player, benched)
                continue
            # keep the one with a player page (more data)
            drop = player if (other[0] in arc.players or player not in arc.players) else other[0]
            members[:] = [m for m in members if m[0] != drop]
            if drop == other[0]:
                seen_nick[nick] = (player, benched)
    # Redirected names ("Lucas" for "LUCAS1"): two members with similar nicks who never appear on
    # the same tournament roster are one player seen under two names (real teammates always do).
    def similar(a: str, b: str) -> bool:
        return bool(a and b) and (a.startswith(b) or b.startswith(a)) and abs(len(a) - len(b)) <= 3
    for team, members in roster.items():
        for p1, _ in list(members):
            for p2, _ in list(members):
                if p1 >= p2 or p1 not in [m[0] for m in members] or p2 not in [m[0] for m in members]:
                    continue
                n1 = norm(arc.info.get(p1, {}).get("nick") or p1.removeprefix("id:"))
                n2 = norm(arc.info.get(p2, {}).get("nick") or p2.removeprefix("id:"))
                if similar(n1, n2) and not (set(arc.played.get(p1, {})) & set(arc.played.get(p2, {}))):
                    keep_first = p1 in arc.players or (p2 not in arc.players and len(arc.spells.get(p1, [])) >= len(arc.spells.get(p2, [])))
                    drop = p2 if keep_first else p1
                    members[:] = [m for m in members if m[0] != drop]
    teams = {t: ps for t, ps in roster.items() if len(ps) >= 4}

    # Strength on the game's scale.
    team_raw = {t: arc.team_score(t, at) for t in teams}
    team_rating = quantile_map(team_raw, ref.ratings)
    team_ers = quantile_map(team_raw, ref.ers)
    players = {p: t for t, ps in teams.items() for p, _ in ps}
    tmax = max(team_raw.values(), default=0) or 1
    pmax = max((arc.player_score(p, at) for p in players), default=0) or 1
    player_raw = {p: 0.6 * arc.player_score(p, at) / pmax + 0.4 * team_raw[t] / tmax for p, t in players.items()}
    skill = quantile_map(player_raw, ref.skills)

    # Team nicks (the reference's short names where the team is known).
    nick_of, used = {}, set()
    for t in sorted(teams, key=lambda t: -team_raw[t]):
        n = base = ref.team_nick.get(t.lower()) or short_team(t)
        k = 1
        while n in used:  # the game names image files after nicks: no symbols
            k += 1
            n = f"{base} {k}"
        used.add(n)
        nick_of[t] = n

    player_rows, player_nicks, order = [], set(), {}
    for team in sorted(teams, key=lambda t: -team_raw[t]):
        members = sorted(teams[team], key=lambda pb: (pb[1], -skill.get(pb[0], 0)))
        rows = [_player_row(p, team, skill[p], ref, arc, at_date, nick_of[team], player_nicks) for p, _ in members]
        _fill_roles(rows)
        for i in range(len(rows), 5):  # a team needs five players
            rows.append(_generated_player(team, i, team_rating[team], ref, nick_of[team], player_nicks, at_date))
        player_rows += rows
        order[nick_of[team]] = [r["Nick"] for r in rows]

    team_rows = []
    for team in sorted(teams, key=lambda t: -team_raw[t]):
        countries = Counter(r["Country"] for r in player_rows if r["Team"] == nick_of[team] and r["Country"])
        igl = next((r["Nick"] for r in player_rows if r["Team"] == nick_of[team] and "In-game leader" in (r["Role1"], r["Role2"], r["Role3"])), "")
        rec = arc.teams.get(team, {})
        team_rows.append({
            "Nick": nick_of[team], "Name": team, "Country": countries.most_common(1)[0][0] if countries else "",
            "Earnings": arc.earnings(team, at), "Rating": team_rating[team], "ERS": team_ers[team],
            "Description": "", "Academy": "", "BgColor": "#%06x" % (stable(team) & 0xFFFFFF), "StartIGL": igl,
            "Female": "1" if "female" in team.lower() or "female" in (rec.get("name") or "").lower() else "",
        })
    names = {t["Name"]: t["Nick"] for t in team_rows}
    for t in team_rows:  # academies: "X Academy" next to X
        base = re.sub(r" Academy$", "", t["Name"])
        if base != t["Name"] and base in names:
            next(r for r in team_rows if r["Nick"] == names[base])["Academy"] = t["Nick"]

    staff_rows = _coaches(teams, nick_of, arc, ref, at)
    return {"players": player_rows, "teams": team_rows, "staff": staff_rows, "order": order,
            "raw": player_raw, "skill": skill, "team_raw": team_raw}


def _player_row(player: str, team: str, skill: int, ref: Reference, arc: Archive, at_date: dt.date,
                team_nick: str, used: set) -> dict:
    info, page_rec = arc.info.get(player, {}), arc.players.get(player, {})
    nick = (info.get("nick") or player.removeprefix("id:")).replace(";", ",")
    page = None if player.startswith("id:") else player
    known = ref.find(page, nick)
    roles = roles_from(page_rec.get("role")) or ([known["Role1"], known["Role2"], known["Role3"]] if known else [])
    roles = [r for r in roles if r][:3]
    if known:
        row = dict(known)
        shift = skill - int(known["Skill"] or skill)
    else:
        row = dict(ref.donor(skill, roles[0] if roles else "Rifler", player))
        shift = skill - int(row["Skill"] or skill)
        name = clean_name(page_rec.get("romanized") or page_rec.get("name") or info.get("name"))
        first, _, last = name.partition(" ")
        row.update({"Name": first, "Surname": last, "Bio": "", "HLTV": "", "Faceit": "", "PhotoUrl": "",
                    "Liquipedia": f"https://liquipedia.net/counterstrike/{page.replace(' ', '_')}" if page else "",
                    "Birthdate": "", "Country": ""})
    for a in ABILITY:
        if str(row.get(a, "")).isdigit():
            row[a] = max(1, min(20, int(row[a]) + shift))
    born = page_rec.get("born")
    if born:
        b = dt.date.fromordinal(day(born))
        row["Birthdate"] = b.strftime("%d.%m.%Y")
    elif not row.get("Birthdate"):
        first = min((d for d, _ in arc.evidence.get(player, [])), default=at_date.toordinal())
        row["Birthdate"] = dt.date(dt.date.fromordinal(first).year - 19, 7, 1).strftime("%d.%m.%Y")
    age = _age(row["Birthdate"], at_date)
    row["Skill"] = skill
    row["Potential"] = min(20, skill + max(0, 23 - age) // 2) if age < 23 else skill
    row["Country"] = info.get("country") or row.get("Country") or ""
    nick = unique(nick, used)
    row.update({"Nick": nick, "Team": team_nick, "Retired": "", "Earnings": 0, "Id": nick,
                "Role1": roles[0] if roles else row.get("Role1", "Rifler"),
                "Role2": roles[1] if len(roles) > 1 else "", "Role3": roles[2] if len(roles) > 2 else "",
                "CreatedBy": "StoryMode", "CreatedAt": ""})
    return row


def unique(nick: str, used: set) -> str:
    """Nicks name the game's image files, so duplicates get a number, never a symbol."""
    out, k = nick, 1
    while out.lower() in used:
        k += 1
        out = f"{nick}{k}"
    used.add(out.lower())
    return out


def _age(birth: str, at: dt.date) -> int:
    d, m, y = (int(x) for x in birth.split("."))
    return at.year - y - ((at.month, at.day) < (m, d))


def _fill_roles(rows: list[dict]) -> None:
    """Every team gets one AWPer and one in-game leader (historical roles are often unknown)."""
    def has(role):
        return any(role in (r["Role1"], r["Role2"]) for r in rows)
    if rows and not has("AWPer"):
        best = max(rows[:5], key=lambda r: int(r.get("AWP") or 0))
        best["Role2"], best["Role1"] = best["Role1"] if best["Role1"] != "AWPer" else "", "AWPer"
    if rows and not has("In-game leader"):
        cands = [r for r in rows[:5] if r["Role1"] != "AWPer"] or rows[:5]
        lead = max(cands, key=lambda r: int(r.get("Leader") or 0) + int(r.get("Tactic") or 0))
        lead["Role2"] = "In-game leader"


def _generated_player(team: str, i: int, rating: int, ref: Reference, team_nick: str, used: set, at: dt.date) -> dict:
    seed = f"{team}#{i}"
    skill = max(1, min(20, rating - 2 + stable(seed) % 3))
    row = dict(ref.donor(skill, "Rifler", seed))
    nick = unique(f"{team_nick[:6]}_{i + 1}", used)
    row.update({"Nick": nick, "Name": "", "Surname": "", "Team": team_nick, "Skill": skill, "Potential": skill,
                "Bio": "", "Id": nick, "HLTV": "", "Liquipedia": "", "Faceit": "", "PhotoUrl": "", "Retired": "",
                "Earnings": 0, "Role1": "Rifler", "Role2": "", "Role3": "",
                "Birthdate": dt.date(at.year - 20 - stable(seed, "age") % 6, 1 + stable(seed, "m") % 12, 1).strftime("%d.%m.%Y"),
                "CreatedBy": "StoryMode-generated", "CreatedAt": ""})
    return row


def _coaches(teams: dict, nick_of: dict, arc: Archive, ref: Reference, at: int) -> list[dict]:
    rows = []
    for team in teams:
        for s in arc.staff.get(team, []):
            join, leave = day(s.get("join")), day(s.get("leave"))
            if "coach" not in (s.get("position") or "").lower() or join is None or join > at or (leave and leave <= at):
                continue
            donor = dict(ref.coaches[stable(s["id"]) % len(ref.coaches)]) if ref.coaches else {}
            first, _, last = clean_name(s.get("name")).partition(" ")
            donor.update({"Nick": s["id"], "Name": first, "Surname": last, "Role": "Coach",
                          "Country": s.get("country") or "", "Team": nick_of[team], "Generated": "0",
                          "Liquipedia": "", "HLTV": "", "PhotoUrl": "", "Id": s["id"], "CreatedBy": "StoryMode", "CreatedAt": ""})
            rows.append(donor)
            break  # one head coach per team
    return rows


# ---------------------------------------------------------------- main

def spearman(xs: list[float], ys: list[float]) -> float:
    def ranks(v):
        order = sorted(range(len(v)), key=lambda i: v[i])
        r = [0.0] * len(v)
        i = 0
        while i < len(order):
            j = i
            while j + 1 < len(order) and v[order[j + 1]] == v[order[i]]:
                j += 1
            for k in range(i, j + 1):
                r[order[k]] = (i + j) / 2
            i = j + 1
        return r
    rx, ry = ranks(xs), ranks(ys)
    n = len(xs)
    mx, my = sum(rx) / n, sum(ry) / n
    cov = sum((a - mx) * (b - my) for a, b in zip(rx, ry))
    var = (sum((a - mx) ** 2 for a in rx) * sum((b - my) ** 2 for b in ry)) ** 0.5
    return cov / var if var else 0.0


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("date", help="world date, YYYY-MM-DD")
    ap.add_argument("--reference", type=Path, default=DEFAULT_REFERENCE)
    ap.add_argument("--out", type=Path)
    ap.add_argument("--calibrate", action="store_true", help="compare skills with the reference database")
    ap.add_argument("--install", action="store_true", help="also install the world as a Story Mode start pack in the game")
    args = ap.parse_args()

    ref, arc = Reference(args.reference), Archive()
    world = build(args.date, ref, arc)
    out = args.out or OUT / args.date
    out.mkdir(parents=True, exist_ok=True)
    write_table(out / "Players.csv", ref.player_header, world["players"])
    write_table(out / "Teams.csv", ref.team_header, world["teams"])
    write_table(out / "Staff.csv", ref.staff_header, world["staff"])
    (out / "roster_order.json").write_text(json.dumps(world["order"], ensure_ascii=False), encoding="utf-8")
    for name in ("Tournaments.csv", "Sponsors.csv"):
        (out / name).write_bytes((args.reference / name).read_bytes())

    lines = [f"world on {args.date}: {len(world['teams'])} teams, {len(world['players'])} players, {len(world['staff'])} coaches",
             f"archive: {len(arc.tournaments)} tournaments, {len(arc.teams)} teams, {len(arc.players)} player pages"]
    lines += [f"  {t['Rating']:>2} {t['ERS']:>5}  {t['Name']}  ({', '.join(world['order'][t['Nick']][:5])})" for t in world["teams"][:25]]
    if args.calibrate:
        pairs = []
        for p, s in world["skill"].items():
            info = arc.info.get(p, {})
            known = ref.find(None if p.startswith("id:") else p, info.get("nick") or "")
            if known and known["Skill"].isdigit():
                pairs.append((s, int(known["Skill"]), info.get("nick")))
        if len(pairs) > 2:
            mae = sum(abs(a - b) for a, b, _ in pairs) / len(pairs)
            lines.append(f"calibration: {len(pairs)} matched players, Spearman {spearman([a for a, _, _ in pairs], [b for _, b, _ in pairs]):.2f}, mean abs error {mae:.2f}")
            worst = sorted(pairs, key=lambda x: -abs(x[0] - x[1]))[:10]
            lines.append("  worst: " + ", ".join(f"{n} {a} vs {b}" for a, b, n in worst))
    report = "\n".join(lines)
    (out / "report.txt").write_text(report, encoding="utf-8")
    print(report)
    if args.install:
        pack = GAME / "plugins" / "RealismOverhaul" / "story" / args.date
        pack.mkdir(parents=True, exist_ok=True)
        for f in out.iterdir():
            if f.suffix in (".csv", ".json"):
                (pack / f.name).write_bytes(f.read_bytes())
        print(f"installed start pack {pack}")


if __name__ == "__main__":
    main()
