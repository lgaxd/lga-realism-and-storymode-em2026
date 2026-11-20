# Story Mode data: Liquipedia content read from Internet Archive copies

Story Mode needs every team, player, roster change and tournament since 2000. Liquipedia has by far the most complete record, under CC-BY-SA 3.0, but its terms forbid automated access to its HTML pages, say its MediaWiki API is not for non-editing use, and its data API (LiquipediaDB) is enterprise-only. So we never touch liquipedia.net: we read the copies of its pages kept by the Internet Archive (Wayback Machine), through the Archive's own CDX and snapshot endpoints at a polite rate, and reuse the content under its licence. The derived dataset is CC-BY-SA 3.0 with attribution to Liquipedia, licensed separately from the MIT code. Wikidata (CC0) and Wikipedia fill and cross-check gaps; hand-curated files cover what neither has (mostly 2000–2005); the game's own generator fills the lower tiers.

## Considered Options

- **Liquipedia MediaWiki API or HTML**: against its terms of use.
- **LiquipediaDB**: enterprise licence only.
- **Esports Earnings API**: registration needs manual approval that did not come.
- **HLTV**: its terms forbid scraping, and its content is not openly licensed.
- **Common Crawl**: no Liquipedia captures in recent collections.

## Consequences

- The raw archive cache stays local (gitignored); only the derived dataset is committed, under its own CC-BY-SA licence file.
- Collection is slow (about one request per second; tens of thousands of pages) and must be resumable.
- Archived pages can be stale or missing: every record carries its source and capture date, and conflicts are resolved by date and confidence.
- Team logos come from the same archive but never enter the repository; they are installed locally into the game's CustomAssets, and a setting disables them for the publishable version.
