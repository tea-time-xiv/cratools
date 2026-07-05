# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Cratools is a Dalamud (FFXIV/XIVLauncher) plugin. MVP feature: paste Teamcraft's "inventory
cleanup" text into the `/cratools` window; the plugin dims ("fades") the inventory slots you need
to keep in the open-all-bags window, so the removable items stand out.

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
4. **`InventoryHighlighter.cs`** — the overlay. `MainWindow` feeds it the resolved removable
   RowIds; each frame it dims the keeper slots.

`Configuration.cs` (`IPluginConfiguration`) is the persisted state (`HighlightEnabled`,
`FadeOpacity`); `Configuration.Save()` calls `PluginInterface.SavePluginConfig`.

### The one non-obvious mechanism (InventoryHighlighter)

Getting the fade onto the *right* slots took several dead ends; do not undo these:

- The open-all-bags window `InventoryExpansion` is only a frame (no slots). The item slots live in
  four separate grid addons **`InventoryGrid0E`–`InventoryGrid3E`**.
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

## Conventions

- Match the safetylock plugin (`../safetylock`) it was scaffolded from: static `[PluginService]`
  services, `WindowSystem` + `Dalamud.Interface.Windowing.Window` subclasses.
- ImGui is `using Dalamud.Bindings.ImGui;` (SDK 15 binding), **not** ImGuiNET.
- Git commits use the Tea Time identity (`Tea Time <tea-time-13371235@proton.me>`); default branch
  is `master`; remote is `https://github.com/tea-time-xiv/cratools.git`.
