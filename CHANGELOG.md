# Changelog

Each released version gets a section here. The publish pipeline copies the section for the
version being released into the GitHub release notes and into the Tea Time plugin repo, which
is what the in-game changelog (`/xlplugins` -> Changelog) shows. Dalamud renders that text as
plain text, so keep it to short `-` bullets: no tables, no links, no bold.

## Unreleased

- Armory cleanup flags spare copies of gear you already keep one of.

## 0.2.0.0

- Armoury cleanup scan: marks gear no class you play can wear, or that you already own
  something better than, and tints it in the Armoury Chest.
- Armoury cleanup lives in the main window as its own tab.

## 0.1.0.0

First release.

- Paste a Teamcraft inventory-cleanup list and Cratools fades the slots you need to keep,
  so the removable items stand out.
- Read-only: it never moves or discards anything.
