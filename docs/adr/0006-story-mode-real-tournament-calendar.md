# Story Mode: the real tournament calendar, every event, real prize money

A Story Mode career plays history's tournaments (ADR 0002). The game itself keeps one list of tournament templates and, every year, copies it and spreads the copies over the year by tier. In a Story Mode career, each year that history has data for gets that year's real events instead: names, tiers, end dates, LAN or online, venues and prize pools. Who plays still comes from the career's own rankings, and each event uses the game's format for its tier.

## Decision

- **Every event**: all S, A, B and C-Tier events of the Main Circuit, not a selection shaped like the game's calendar. Tiers map as Valve Major → Major, S → T1, A → T2, B → T3, C → T4. Before the first Valve Major (DreamHack Winter 2013) a year has no Major.
- **Real prize money**: each event pays its real prize pool; when the archive has none, the game prices it by tier as usual.
- Left out: qualifiers, weeklies and monthlies, showmatches, national-team and female-only events (the game cannot restrict entry), CS:Source, and events that were cancelled.
- History before the Start Date is not replayed: the first year only gets the events from that date on. Years after the data, and normal careers, keep the game's calendar.
- The calendar is one file for all packs (story/calendar.json), built by tools/storydata/tour_calendar.py.

## Considered Options

- **The game's shape** (about 51 events a year, the biggest real ones per tier): closer to the game's balance, but real top events would be left out in busy years.
- **All S and A, top B and C**: a middle ground; the player preferred the full calendar.
- **The game's prize money by tier**: a stable economy, but a 2004 CPL would pay like a 2025 event.

## Consequences

- From about 2016 a year holds hundreds of events; the simulation, e-mails and calendar views carry far more than in a normal career.
- The 2000s pay little, as they did; contract and budget levels of the time are Era work (ADR 0005) still to do.
- Real formats (group sizes, Swiss stages, best-of) are not modelled yet.
