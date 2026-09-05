# Changelog

Each released version gets a section here. The publish pipeline copies the section for the
version being released into the GitHub release notes and into the Tea Time plugin repo, which
is what the in-game changelog (`/xlplugins` -> Changelog) shows. Dalamud renders that text as
plain text, so keep it to short `-` bullets: no tables, no links, no bold.

## Unreleased

- Armory cleanup flags spare copies of gear you already keep one of.
- New Glamour collection tab: finds gear in your armoury, bags or worn that can still be stored
  as a glamour outfit set but is not in the dresser yet, so you keep it instead of scrapping it.
- Gear that is not collected yet is outlined gold in the Armoury Chest and in your bags. Junk
  gear that is also worth storing keeps its red tint and gains the outline.
- Results are grouped by outfit, with how many of its pieces the dresser already holds.
- Armoire-eligible gear is listed separately as an armoire deposit, since the dresser refuses it.
- Pieces the dresser would refuse right now are marked blocked, with the reason.
- The inventory fade now works in all three inventory layouts, not only the open-all-bags window.

## 0.2.0.0

- Armoury cleanup scan: marks gear no class you play can wear, or that you already own
  something better than, and tints it in the Armoury Chest.
- Armoury cleanup lives in the main window as its own tab.

## 0.1.0.0

First release.

- Paste a Teamcraft inventory-cleanup list and Cratools fades the slots you need to keep,
  so the removable items stand out.
- Read-only: it never moves or discards anything.
