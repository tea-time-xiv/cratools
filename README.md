# Cratools

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for FFXIV that highlights items you no
longer need, so you can clear space without reading every tooltip.

Two features so far:

- **Inventory cleanup** — paste a [Teamcraft](https://ffxivteamcraft.com/) inventory-cleanup list
  and the slots you need to *keep* are dimmed in the open-all-bags window, leaving the removable
  items bright.
- **Armory cleanup** — scans your Armoury Chest and marks gear that is redundant: equipment no
  class you play can wear, and equipment you already own something better than.

Cratools is **strictly read-only**. It reads your inventory and draws over the game's windows. It
never moves, discards, sells, or desynthesises anything, and it never writes to game memory — so
there is nothing to undo, and nothing to reset if it crashes or is unloaded.

## Install

Not in a plugin repository yet. To run it, build it (below) and dev-load it:

1. `/xlsettings` → **Experimental** → **Dev Plugin Locations**
2. Add the path to `Cratools\bin\x64\Release\Cratools.dll`
3. `/cratools`

## Usage

Everything lives in one window, opened with `/cratools` or the plugin installer's main-UI button.

| Command | Does |
| --- | --- |
| `/cratools` | Open the window |
| `/cratools armory` | Open it on the Armory cleanup tab |
| `/cratools armorydump` | Log armoury diagnostics to `dalamud.log` |

### Inventory cleanup

Paste the list Teamcraft gives you, press **Apply**, then open all bags. Keepers are dimmed. Names
that could not be matched to an item are listed so you can spot typos or localisation mismatches.

### Armory cleanup

Press **Scan armoury**. Every piece is listed with the verdict and the reason for it — read the
*Why* column before trusting anything. Junk is also tinted red in the Armoury Chest itself.

An item is called junk for one of two reasons:

- **Locked class** — no class or job you play can equip it at all. This is where most of the value
  is: weapons serve a single class, so every weapon for a class you never unlocked is dead weight.
- **Outclassed** — for *every* job you play that could wear it, you already own something better.

"Better" is deliberately narrow. A candidate must be the same slot, usable by that job, wearable at
that job's *current level*, of higher item level, **and** of the same role — either an identical
`ClassJobCategory` or an identical STR/DEX/VIT/INT/MND profile. Item level alone is not enough: it
would let an ilvl 130 tank helm supersede an ilvl 120 healer helm.

Because junk requires an upgrade for every job, a single holdout keeps an item. If you have PGL at
20 and LNC at 40, your level 19 earrings stay — LNC has moved on, PGL has not.

**Nothing is ever junk if** it is used by a gearset, currently equipped, melded, glamoured, dyed,
unique, rare, or on your keep list. Right-click any row to pin or unpin an item.

#### Ignore classes below level

Accessories are wearable by nearly every job, so one class parked at level 1 keeps every accessory
you own "still the best" for it, and nothing is ever redundant. Raise this setting to stop counting
classes you do not really play.

It cuts both ways: those classes stop holding shared gear hostage, but gear only they can use starts
counting as junk. Pick the number deliberately.

## Build

Requires the **.NET 10 SDK** and a Dalamud dev install (the plugin SDK resolves Dalamud,
FFXIVClientStructs and ImGui from `%AppData%\XIVLauncher\addon\Hooks\dev`, which XIVLauncher
populates when you enable plugin development).

```
dotnet build Cratools.sln -c Release
```

`global.json` pins the SDK version. The project targets `net10.0`, x64, Dalamud API level 15.

There are no automated tests; verification is in-game.

## Licence

AGPL-3.0-or-later.
