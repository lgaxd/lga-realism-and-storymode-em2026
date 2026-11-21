"""Turn archived Liquipedia pages into structured records (team, player, tournament).

Every record keeps its source page and capture date (ADR 0004), so conflicts between sources can
be resolved later by date and confidence. Dates stay as strings in "YYYY", "YYYY-MM" or
"YYYY-MM-DD" form; unknown parts ("2000-06-??") are trimmed to the known precision.
"""
from __future__ import annotations

import re
import urllib.parse

from bs4 import BeautifulSoup, Tag

_REF = re.compile(r"\[\s*\d+\s*\]")
_DATE = re.compile(r"(\d{4})(?:-(\d{2}|\?\?))?(?:-(\d{2}|\?\?))?")
_MONEY = re.compile(r"\$\s*([\d,]+)")


def clean(text: str | None) -> str:
    return re.sub(r"\s+", " ", _REF.sub("", text or "")).strip()


def date(text: str | None) -> str | None:
    """'2003-10-12' / '2000-06-??' / 'June 10, 1988' -> 'YYYY[-MM[-DD]]' or None."""
    t = clean(text)
    months = ["january", "february", "march", "april", "may", "june", "july", "august",
              "september", "october", "november", "december"]
    m = re.search(r"([A-Za-z]+)\s+(\d{1,2}),\s*(\d{4})", t)
    if m and m.group(1).lower() in months:
        return f"{m.group(3)}-{months.index(m.group(1).lower()) + 1:02d}-{int(m.group(2)):02d}"
    m = _DATE.search(t)
    if m:
        y, mo, d = m.groups()
        if mo and mo != "??":
            return f"{y}-{mo}-{d}" if d and d != "??" else f"{y}-{mo}"
        return y
    return None


def money(text: str | None) -> int | None:
    m = _MONEY.search(text or "")
    return int(m.group(1).replace(",", "")) if m else None


def link_title(a: Tag | None) -> str | None:
    """The Liquipedia page a link points to ('/counterstrike/Ninjas_in_Pyjamas' -> 'Ninjas in Pyjamas')."""
    if a is None or not a.get("href"):
        return None
    href = a["href"]
    if "/counterstrike/" not in href or "redlink" in href:
        return None
    path = urllib.parse.unquote(href.split("/counterstrike/", 1)[1].split("#")[0])
    return path.replace("_", " ") or None


def infobox(soup: BeautifulSoup) -> dict[str, str]:
    box = soup.select_one(".fo-nttax-infobox")
    result: dict[str, str] = {}
    if box is None:
        return result
    for label in box.select(".infobox-cell-2"):
        value = label.find_next_sibling()
        key = clean(label.get_text(" ")).rstrip(":")
        if key and value is not None:
            result[key] = clean(value.get_text(" "))
    return result


def flag_country(cell: Tag | None) -> str | None:
    """Country from a flag icon (<span class="flag"><img alt="Sweden">)."""
    if cell is None:
        return None
    img = cell.select_one(".flag img, img[src*='flag'], img[alt]")
    return clean(img.get("alt")) if img is not None and img.get("alt") else None


def kind(soup: BeautifulSoup) -> str | None:
    """'tournament', 'team', 'player' or None, from the infobox fields."""
    ib = infobox(soup)
    if not ib:
        return None
    if "Liquipedia Tier" in ib or ("Start Date" in ib and "Prize Pool" in ib):
        return "tournament"
    if "Nationality" in ib or "Born" in ib:
        return "player"
    if "Created" in ib or "Region" in ib or soup.find(id="Player_Roster"):
        return "team"
    return None


def _table_rows(table: Tag) -> tuple[list[str], list[Tag]]:
    rows = table.select("tr")
    if not rows:
        return [], []
    head: list[str] = []
    for c in rows[0].select("th,td"):
        # A header spanning several columns ("Tournament" over icon + name) names its last column;
        # the columns before it are icon cells with no name.
        span = int(c.get("colspan", 1) or 1)
        head.extend([""] * (span - 1) + [clean(c.get_text(" "))])
    body = [r for r in rows[1:] if r.select("td")]
    return head, body


def _section(table: Tag) -> str:
    """The nearest heading above a table ('Active', 'Former', 'Organization', 'Results'...)."""
    h = table.find_previous(["h2", "h3", "h4"])
    return clean(h.get_text(" ")) if h else ""


def _results(soup: BeautifulSoup) -> list[dict]:
    """The Results table of a player or team page (placements; not the match list)."""
    results = []
    for table in soup.select("table.wikitable, table.table2__table"):
        head, body = _table_rows(table)
        if not {"Date", "Tournament", "Place"} <= set(head):
            continue
        for row in body:
            cells = row.select("td")
            if len(cells) < len(head):
                continue
            col = {h: cells[i] for i, h in enumerate(head) if h}
            results.append({
                "date": date(col["Date"].get_text(" ")),
                "place": clean(col["Place"].get_text(" ")),
                "tier": clean(col["Tier"].get_text(" ")) if "Tier" in col else None,
                "type": clean(col["Type"].get_text(" ")) if "Type" in col else None,
                "tournament": link_title(col["Tournament"].select_one("a")) or clean(col["Tournament"].get_text(" ")),
                "team": link_title(col["Team"].select_one("a")) if "Team" in col else None,
                "prize": money(col["Prize"].get_text(" ")) if "Prize" in col else None,
            })
    return results


def _logo(soup: BeautifulSoup) -> str | None:
    """The team logo in the infobox (the dark-mode version suits the game's dark UI)."""
    imgs = [i.get("src") for i in soup.select(".infobox-image img") if i.get("src")]
    dark = [s for s in imgs if "darkmode" in s]
    return (dark or imgs or [None])[0]


def parse_team(soup: BeautifulSoup) -> dict:
    ib = infobox(soup)
    name = clean(soup.select_one("h1").get_text(" ")) if soup.select_one("h1") else ""
    members, staff = [], []
    for table in soup.select("table.table2__table, table.wikitable"):
        head, body = _table_rows(table)
        if "Join Date" not in head:
            continue
        section = _section(table)
        is_staff = "Organization" in [clean(h.get_text(" ")) for h in table.find_all_previous(["h2", "h3"], limit=2)]
        for row in body:
            cells = row.select("td")
            if len(cells) < len(head):
                continue
            col = {h: cells[i] for i, h in enumerate(head) if h}
            # The unnamed column right after Name holds the position ("Head Coach", "Founder").
            if "Name" in head and head.index("Name") + 1 < len(head) and head[head.index("Name") + 1] == "":
                col["Position"] = cells[head.index("Name") + 1]
            id_cell = col.get("ID")
            record = {
                "id": clean(id_cell.get_text(" ")) if id_cell else None,
                "page": link_title(id_cell.select_one("a") if id_cell else None),
                "name": clean(col["Name"].get_text(" ")) if "Name" in col else None,
                "position": clean(col["Position"].get_text(" ")) if "Position" in col else None,
                "country": flag_country(id_cell) or flag_country(row),
                "join": date(col["Join Date"].get_text(" ")) if "Join Date" in col else None,
                "inactive": date(col["Inactive Date"].get_text(" ")) if "Inactive Date" in col else None,
                "leave": date(col["Leave Date"].get_text(" ")) if "Leave Date" in col else None,
                "new_team": link_title(col["New Team"].select_one("a")) if "New Team" in col else None,
                "status": section,
            }
            if record["id"]:
                (staff if is_staff else members).append(record)
    created = [d for d in (date(x) for x in re.split(r"[:;]", ib.get("Created", ""))) if d]
    disbanded = [d for d in (date(x) for x in re.split(r"[:;]", ib.get("Disbanded", ""))) if d]
    return {
        "kind": "team", "name": name, "location": ib.get("Location"), "region": ib.get("Region"),
        "created": created, "disbanded": disbanded, "winnings": money(ib.get("Approx. Total Winnings")),
        "igl": ib.get("In-Game Leader"), "coaches": ib.get("Coaches"), "logo": _logo(soup),
        "members": members, "staff": staff, "results": _results(soup),
    }


def _history(soup: BeautifulSoup) -> list[dict]:
    """The infobox History: one team spell per line ('2006-02-08 – 2006-08-??' next to the team)."""
    out = []
    box = soup.select_one(".fo-nttax-infobox")
    for when in (box.select(".th-mono") if box else []):
        team_div = when.find_next_sibling("div")
        if team_div is None:
            continue
        a = team_div.select_one("a")
        team = link_title(a)
        if team is None and a is not None and a.get("title"):  # a team without a page yet
            team = a["title"].replace(" (page does not exist)", "")
        parts = re.split(r"\s+[–-]\s+", clean(when.get_text(" ")))
        out.append({"team": team or clean(team_div.get_text(" ")), "join": date(parts[0]),
                    "leave": date(parts[1]) if len(parts) > 1 else None})
    return out


def parse_player(soup: BeautifulSoup) -> dict:
    ib = infobox(soup)
    nick = clean(soup.select_one("h1").get_text(" ")) if soup.select_one("h1") else ""
    results = _results(soup)
    years = re.findall(r"\d{4}", ib.get("Years Active (Player)", ""))
    return {
        "kind": "player", "id": nick, "name": ib.get("Name"), "romanized": ib.get("Romanized Name"),
        "nationality": ib.get("Nationality"), "born": date(ib.get("Born")), "status": ib.get("Status"),
        "role": ib.get("Role") or ib.get("Roles"), "died": date(ib.get("Died")), "history": _history(soup), "team": ib.get("Team"), "alternate_ids": ib.get("Alternate IDs"),
        "active_from": years[0] if years else None, "active_to": years[1] if len(years) > 1 else None,
        "winnings": money(ib.get("Approx. Total Winnings")), "results": results,
        "photo": None if "NoImage" in (_logo(soup) or "NoImage") else _logo(soup),
    }


def parse_tournament(soup: BeautifulSoup) -> dict:
    ib = infobox(soup)
    name = clean(soup.select_one("h1").get_text(" ")) if soup.select_one("h1") else ""
    placements = []
    for table in soup.select(".prizepooltable"):
        place = None
        for row in table.select(".csstable-widget-row"):
            cells = row.select(".csstable-widget-cell")
            if not cells:
                continue
            first = clean(cells[0].get_text(" "))
            if re.match(r"^\d", first) or first.lower().startswith("place"):
                place = first
            prize = next((money(c.get_text(" ")) for c in cells if money(c.get_text(" "))), None)
            for team_cell in row.select(".block-team, .team-template-team-standard, .team-template-team-short"):
                a = team_cell.select_one("a")
                placements.append({"place": place, "team": link_title(a) or clean(team_cell.get_text(" ")), "prize": prize})
    rosters = []
    for card in soup.select(".teamcard"):
        header = card.select_one("center a, .teamcard-header a, a")
        players = []
        for row in card.select("tr"):
            # The first link in a row is the flag (Category:Country); the player link follows it.
            # The first named link that is not a flag or a team icon (loans show the other team's icon).
            a = next((x for x in row.select("a[href*='/counterstrike/']")
                      if x.find_parent(class_="flag") is None and "Category:" not in x.get("href", "")
                      and x.find_parent(class_=re.compile(r"team-template")) is None and clean(x.get_text(" "))), None)
            pos = clean(row.select_one("th").get_text(" ")) if row.select_one("th") else ""
            if a is not None and pos[:1].isdigit():
                players.append({"id": clean(a.get_text(" ")), "page": link_title(a), "country": flag_country(row), "slot": pos})
        rosters.append({"team": link_title(header) or (clean(header.get_text(" ")) if header else None), "players": players})
    return {
        "kind": "tournament", "name": name, "series": ib.get("Series"), "organizer": ib.get("Organizer"),
        "game": ib.get("Game"), "type": ib.get("Type"), "location": ib.get("Location"),
        "prize_pool": money(ib.get("Prize Pool")), "start": date(ib.get("Start Date")), "end": date(ib.get("End Date")),
        "teams": ib.get("Teams"), "tier": ib.get("Liquipedia Tier"), "restrictions": ib.get("Restrictions"),
        "placements": placements, "rosters": rosters,
    }


def parse(html: str) -> dict | None:
    soup = BeautifulSoup(html, "lxml")
    k = kind(soup)
    if k == "team":
        return parse_team(soup)
    if k == "player":
        return parse_player(soup)
    if k == "tournament":
        return parse_tournament(soup)
    return None
