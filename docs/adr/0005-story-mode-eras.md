# Story Mode eras: the game looks and plays like its time

A Story Mode career runs from 2000 to today, and Counter-Strike changed beyond recognition in that span: in 2002 a top team was five friends with a hardware sponsor and a LAN bag; in 2024 it is a company with analysts, a psychologist, a training facility and a content team. Story Mode splits history into five Eras, and each Era changes both how the game looks and what an organization can do, in the spirit of management games where the studio grows through the decades.

## Decision

- **Five Eras**: LAN Party (2000 – Sept 2003, CS 1.0–1.5), 1.6 Golden Age (Sept 2003 – Aug 2012), Global Offensive (Aug 2012 – Mar 2016), Esports Boom (Mar 2016 – Sept 2023, from the first $1M Major), CS2 (Sept 2023 on). The boundaries are Game Updates already in `data/story/game_updates.json`.
- **Look**: each Era has an Era Theme: a colour palette, fonts and generated panel textures (bevelled grey panels in the 2000s, flat dark UI in the 2010s, glassy near-black today). Layouts never change, so the game's screens stay usable and game updates do not break the themes. The theme is applied at runtime by recolouring the game's own palette colours and nothing else, so team colours, rating colours and other meaningful colours keep their meaning.
- **Play**: an Org Upgrade (an analyst, a gaming house, a media team...) becomes possible on its real date, but an organization only gets it by paying to adopt it. Early adopters gain an edge; nobody gets anything for free when history reaches it. Areas that change by Era: money and contracts, staff and facilities, information and media, competition rules.
- The definitions live in `data/story/eras.json`; behaviour reads them instead of hard-coding years.

## Considered Options

- **Three Eras by game version**: simpler, but 2004 and 2011 would look and play the same.
- **Palette and fonts only**: safest, but the 2000s would not feel different enough.
- **Full reskin with layout changes**: most striking, most fragile, and much more work per Era.
- **Unlocks purely by date**: no decisions for the player.
- **A research tree**: closest to a tycoon game, but lets organizations get ahead of history.

## Consequences

- The theme engine must recognise the game's palette colours: a diagnostic census of the colours, sprites and fonts in use comes first, and the Era palettes map those colours.
- Era Themes can be previewed in a normal career through a diagnostic setting, before Story Mode's loader exists.
- Org Upgrades need a home in the sidecar (ADR 0003) and their effects need hooks in the systems they change (sponsors, staff, news, salaries).
