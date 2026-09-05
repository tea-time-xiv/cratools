# Cratools

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for FFXIV that highlights items you no
longer need, so you can clear space without reading every tooltip.

Three features so far:

- **Inventory cleanup** — paste a [Teamcraft](https://ffxivteamcraft.com/) inventory-cleanup list
  and the slots you need to *keep* are dimmed in your inventory, leaving the removable items
  bright.
- **Armory cleanup** — scans your Armoury Chest and marks gear that is redundant: equipment no
  class you play can wear, and equipment you already own something better than.
- **Glamour collection** — the other direction: marks gear you are about to throw away that the
  glamour dresser has never seen, and that can still be stored as an outfit set.

Cratools is **strictly read-only**. It reads your inventory and draws over the game's windows. It
never moves, discards, sells, or desynthesises anything, and it never writes to game memory — so
there is nothing to undo, and nothing to reset if it crashes or is unloaded.

## Install

Requires XIVLauncher, FINAL FANTASY XIV and Dalamud.

1. Add the custom repo to Dalamud:
   `https://raw.githubusercontent.com/tea-time-xiv/pluginmaster/master/pluginmaster.json`
2. Install Cratools from the Dalamud plugin installer.
3. `/cratools`

To run a local build instead, dev-load it: `/xlsettings` → **Experimental** → **Dev Plugin
Locations** → add the path to `Cratools\bin\x64\Release\Cratools.dll`.

## Usage

Everything lives in one window, opened with `/cratools` or the plugin installer's main-UI button.

| Command | Does |
| --- | --- |
| `/cratools` | Open the window |
| `/cratools armory` | Open it on the Armory cleanup tab |
| `/cratools glamour` | Open it on the Glamour collection tab |
| `/cratools armorydump` | Log armoury diagnostics to `dalamud.log` |
| `/cratools glamourdump` | Log glamour dresser diagnostics to `dalamud.log` |

### Inventory cleanup

Paste the list Teamcraft gives you, press **Apply**, then open your bags — any of the three
inventory layouts works. Keepers are dimmed. Names that could not be matched to an item are listed
so you can spot typos or localisation mismatches.

### Armory cleanup

Press **Scan armoury**. Every piece is listed with the verdict and the reason for it — read the
*Why* column before trusting anything. Junk is also tinted red in the Armoury Chest itself.

An item is called junk for one of three reasons:

- **Locked class** — no class or job you play can equip it at all. This is where most of the value
  is: weapons serve a single class, so every weapon for a class you never unlocked is dead weight.
- **Outclassed** — for *every* job you play that could wear it, you already own something better.
- **Spare** — an identical copy of a piece that is already being kept. Rings are exempt, since both
  hands take one and a second copy is a legitimate pair.

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

### Glamour collection

Patch 7.1 lets the glamour dresser store a whole outfit in a single slot, which makes a lot of old
gear worth keeping that used to be worth scrapping. This tab finds it: gear in your armoury, bags
or on your back that belongs to an outfit set the dresser does not hold yet.

Press **Scan for uncollected gear**. Results are grouped by outfit, because a full armoury turns up
a couple of hundred loose pieces and "this outfit needs three more" is the unit the dresser actually
stores in:

```
Halonic Priest's Attire — 3 here, none of 5 stored
Woad Skyraider's Attire — 1 here, 8 of 9 stored
Armoire — 16 to deposit
```

Anything found is outlined **gold** in the Armoury Chest and in your bags. Gear that is *both* junk
and worth storing keeps its red tint and gains the gold outline — "you do not need to wear this,
but do not throw it away either".

A piece is only listed if the game would actually accept it:

- **Armoire gear is never a dresser suggestion.** The dresser refuses anything the armoire can
  hold, so those pieces are listed separately as armoire deposits. The armoire is free and
  unlimited, so that is usually the cheaper half of the job. Toggle it off if you only care about
  the dresser.
- Pieces already in the dresser — loose, or filling their slot in a stored outfit — are not listed.
- Pieces the dresser would refuse right now are listed as **blocked**, with the reason: worn, in a
  gearset, melded, or in need of repair. Use *Hide blocked* to drop them.

**Cratools has to know what your dresser holds.** It reads the game's own dresser cache, which
survives zoning and logging out, so a scan usually works anywhere. If the cache has never been
filled the tab says so — open the glamour dresser once (an inn room will do) and scan again. The
armoire needs opening once per session for the same reason. Scanning with the dresser actually open
reads it exactly rather than from cache.

## Build

Requires the **.NET 10 SDK** and a Dalamud dev install (the plugin SDK resolves Dalamud,
FFXIVClientStructs and ImGui from `%AppData%\XIVLauncher\addon\Hooks\dev`, which XIVLauncher
populates when you enable plugin development).

```
dotnet build Cratools.sln -c Release
```

`global.json` pins the SDK version. The project targets `net10.0`, x64, Dalamud API level 15.

There are no automated tests; verification is in-game.

## Releasing

1. Bump `<Version>` in the csproj and add the matching `## <version>` section to
   `CHANGELOG.md`. CI fails the build without one — that section becomes both the GitHub
   release notes and the in-game changelog.
2. Merge to `master`. CI builds, tags `v<version>` and publishes the GitHub release.
3. The Tea Time plugin repo picks the release up within 15 minutes. To publish at once, run
   its *Publish pluginmaster* workflow: `gh workflow run publish.yml -R tea-time-xiv/pluginmaster`,
   or the **Run workflow** button on that repo's Actions tab. This repo holds no credential
   for it.

## Licence

AGPL-3.0-or-later.
