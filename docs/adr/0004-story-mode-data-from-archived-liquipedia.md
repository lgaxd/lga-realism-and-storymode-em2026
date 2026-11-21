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
- Collection is slow (one request every 4 seconds, which keeps clear of the Archive's throttling; about 24,000 pages, so roughly a day) and must be resumable. Each page is fetched at its exact latest capture, and pages the Archive never captured are never requested.
- Archived pages can be stale or missing: every record carries its source and capture date, and conflicts are resolved by date and confidence.
- Team logos and player photos come from the same archive but never enter the repository: they live only in the local start packs, and the publishable build ships none. Each start pack carries its own set (the game finds team images by name, and Fnatic's 2005 logo is not its 2016 one); the mod answers the game's image lookups from the pack of the world being played, so nothing is copied into the game's CustomAssets and images the player added there are never touched.
