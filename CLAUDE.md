# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Cratools is a Dalamud (FFXIV/XIVLauncher) plugin. Three features, all strictly read-only overlays
over the game's own windows:

- **Inventory cleanup** (the MVP): paste Teamcraft's "inventory cleanup" text into the `/cratools`
  window; the plugin dims ("fades") the inventory slots you need to keep, so the removable items
  stand out.
- **Armory cleanup**: marks armoury gear no unlocked class can wear, or that better owned gear
  supersedes, red in the Armoury Chest.
- **Glamour collection**: the inverse — marks gear that is *not* in the glamour dresser yet and
  can still be stored as an outfit set, gold, so it is kept rather than scrapped.

## Build

This machine has **no global .NET 10 SDK** (installing it globally breaks other software). A
portable .NET 10 SDK lives at `%USERPROFILE%\dotnet10` and must be invoked by full path — a bare
`dotnet build` hits global SDK 9 and fails on the net10 `Dalamud.NET.Sdk`.

```powershell
& "$env:USERPROFILE\dotnet10\dotnet.exe" build Cratools.sln -c Debug
& "$env:USERPROFILE\dotnet10\dotnet.exe" build Cratools.sln -c Release
```

`global.json` pins SDK `10.0.301` for IDEs. The project SDK is `Dalamud.NET.Sdk/15.0.0`
(net10.0, x64, Dalamud API level 15) — it auto-resolves Dalamud, FFXIVClientStructs, ImGui, and
the dev Dalamud from `%AppData%\XIVLauncher\addon\Hooks\dev`. There is no `DALAMUD_HOME`, no
DalamudPackager.

There are no tests. Verification is in-game: dev-load `Cratools\bin\x64\Debug\Cratools.dll` via
`/xlsettings` → Experimental → Dev Plugin Locations, then `/cratools`.

## Architecture

Data flows one direction, `Plugin.cs` wiring it together:

1. **`Plugin.cs`** — `IDalamudPlugin` entry point. Holds all Dalamud services as static
   `[PluginService]` properties. Registers `/cratools`, the `WindowSystem`, and hooks
   `UiBuilder.Draw` for both the windows and `InventoryHighlighter.Draw` (per-frame overlay).
2. **`CleanupList.cs`** — parses the pasted Teamcraft text (blank-line-separated blocks of a name
   line + optional `xN`) into `CleanupEntry` items.
3. **`ItemResolver.cs`** — resolves item names → item RowId via the Lumina `Item` sheet
   (`IDataManager`), built once, case-insensitive. HQ items share the base RowId.
4. **`InventoryHighlighter.cs`** — the bags overlay. `MainWindow` feeds it the resolved removable
   RowIds and `GlamourTab` feeds it the glamour gaps; each frame it dims the keeper slots and
   outlines the gaps.

The armory and glamour features add, under `Armory/`:

5. **`ArmoryScanner.cs`** — reads the armoury containers, the equipped set and (opt-in) the four
   inventory bags into `ArmoryItem` records. **`EquipRules.cs`** / **`JobUnlockState.cs`** /
   **`GearsetIndex.cs`** are the sheet- and character-derived facts the classifiers need.
6. **`ArmoryAnalyzer.cs`** — the junk classifier; **`ArmoryHighlighter.cs`** the Armoury Chest
   overlay for both the junk tint and the glamour outline.
7. **`GlamourSets.cs`** — outfit sets and armoire eligibility from the sheets, built once.
   **`DresserState.cs`** — a read-only snapshot of what the dresser and armoire hold.
   **`GlamourGapFinder.cs`** — the classifier over the two.
8. **`ArmoryDebug.cs`** / **`GlamourDebug.cs`** — `/cratools armorydump` and `/cratools
   glamourdump`. Both exist to pin down addon and memory layouts that only the running game can
   confirm; keep them working, they are the regression check after a patch.

`Configuration.cs` (`IPluginConfiguration`) is the persisted state; `Configuration.Save()` calls
`PluginInterface.SavePluginConfig`.

### The one non-obvious mechanism (InventoryHighlighter)

Getting the fade onto the *right* slots took several dead ends; do not undo these:

- The inventory windows are only frames (no slots). The item slots live in separate grid addons,
  and *which* ones depends on the layout: `InventoryExpansion` (all bags) uses
  **`InventoryGrid0E`–`InventoryGrid3E`**, one per bag; `InventoryLarge` (two bags) uses
  **`InventoryGrid0`/`InventoryGrid1`** showing the bag pair its `TabIndex` selects; `Inventory`
  (one bag) uses **`InventoryGrid`** showing the bag its `TabIndex` selects. Handling only the
  expanded view is the standard way this feature looks broken.
- Don't tree-walk the grids for drag-drop nodes — that returns 70/grid (35 real + 35 hidden
  templates) in the wrong order. Use `AddonInventoryGrid.Slots` (ClientStructs), the ordered
  35-entry `AtkComponentDragDrop` array; `Slots[i]->OwnerNode` is the slot's node for its rect.
- The grid renders items in the player's **sorted display order**, so visual slot `i` is NOT
  container slot `i`. Translate through `ItemOrderModule.Instance()->InventorySorter`: entry
  `Items[g*35 + i]` gives the real `(Page, Slot)`, read via
  `InventoryManager.GetInventoryContainer(Inventory1 + Page)->GetInventorySlot(Slot)`.
- The overlay is strictly **read-only** — it reads node screen rects and draws translucent rects
  on `ImGui.GetBackgroundDrawList()`. It never writes game memory, so there is nothing to reset on
  teardown or when the window closes.

### The other non-obvious mechanism (glamour dresser)

Four facts, each confirmed in-game with `/cratools glamourdump`; none is guessable from the names:

- A stored outfit occupies **one** dresser entry holding the set's *token* item id — the "… Attire"
  row of `MirageStoreSetItem`, whose eleven columns are the pieces in the order MainHand, OffHand,
  Head, Body, Hands, Legs, Feet, Earrings, Necklace, Bracelets, Ring. That column index is the slot
  index every eleven-bit mask in this area uses.
- `ItemFinderModule.IsGlamourDresserCached` is **not** a "has data" flag. It read false over eight
  hundred fully populated entries. Never gate on it; check for non-zero ids instead.
- `ItemFinderModule.GlamourDresserItemSetUnlockBits` marks the slots that are **missing**, the
  complement of `MirageManager.IsSetSlotUnlocked` despite the name (verified 8/8 against 0/8 for
  the opposite reading). One constant, `DresserState.SetBitsMeanMissingSlots`, holds the polarity.
- Armoire-eligible gear (the `Cabinet` sheet) can **never** go in the dresser, so it is never a
  dresser suggestion. Roughly 2300 of the 5900 outfit pieces are armoire items, so skipping this
  check produces thousands of impossible suggestions.

`MirageManager` is authoritative but only populated while the dresser has been opened in the
current zone; `ItemFinderModule` is a saved per-character file that survives zoning and logout.
`DresserState` prefers the first and falls back to the second, which is why nothing has to be
persisted in `Configuration`.

## Conventions

- Match the safetylock plugin (`../safetylock`) it was scaffolded from: static `[PluginService]`
  services, `WindowSystem` + `Dalamud.Interface.Windowing.Window` subclasses.
- ImGui is `using Dalamud.Bindings.ImGui;` (SDK 15 binding), **not** ImGuiNET.
- Git commits use the Tea Time identity (`Tea Time <tea-time-13371235@proton.me>`); default branch
  is `master`; remote is `https://github.com/tea-time-xiv/cratools.git`.
