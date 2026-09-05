using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Cratools;

/// <summary>
/// Marks up the open-all-bags inventory. It fades (dims) the "keeper" slots so the removable items
/// pointed at by the pasted list stay bright, and outlines in gold anything the glamour scan found
/// worth storing.
///
/// The item slots never live in the inventory window itself — they live in separate grid addons,
/// and which ones depends on the layout the player chose. All three are handled, because "my
/// inventory is not highlighted" is otherwise just a matter of which window happens to be open:
///
///  - InventoryExpansion (all four bags at once): grids InventoryGrid0E–3E, grid g showing bag g.
///  - InventoryLarge (two bags side by side): grids InventoryGrid0 and InventoryGrid1, showing the
///    pair of bags its TabIndex selects — tab 1 means bags 2 and 3.
///  - Inventory (one bag): the single grid InventoryGrid, showing the bag its TabIndex selects.
///
/// The grid renders items in the player's sorted display order, so a visual slot is mapped to its
/// real item via ItemOrderModule.InventorySorter (see <see cref="ResolveDisplaySlot"/>).
///
/// The two marks compose: a faded keeper that is also a glamour gap keeps the dim fill and gains
/// the outline, so the gold never blends with the fade into a colour that means neither.
///
/// Read-only: it never writes to game memory. Each frame it reads slot-node screen rects, then
/// draws on the ImGui background draw list. Nothing to reset.
/// </summary>
public sealed unsafe class InventoryHighlighter
{
    private const int SlotsPerBag = 35;

    private const int BagCount = 4;

    // The open-all-bags grid addons (the "E" suffix = expanded view). Grid g shows display page g
    // of the sorter; the other two layouts need their parent window's tab to say which page a grid
    // is showing, so they are resolved per frame in ResolveGrids.
    private static readonly string[] ExpandedGrids =
    {
        "InventoryGrid0E", "InventoryGrid1E", "InventoryGrid2E", "InventoryGrid3E",
    };

    private readonly IGameGui gameGui;
    private readonly Configuration configuration;

    private readonly List<(string Addon, int Page)> resolvedGrids = new(BagCount + 3);

    private HashSet<uint> removableIds = new();
    private HashSet<(InventoryType Container, short Slot)> gapSlots = new();

    public InventoryHighlighter(IGameGui gameGui, Configuration configuration)
    {
        this.gameGui = gameGui;
        this.configuration = configuration;
    }

    public int RemovableCount => removableIds.Count;

    public void SetRemovable(IEnumerable<uint> ids) => removableIds = new HashSet<uint>(ids);

    public void SetGaps(IEnumerable<(InventoryType Container, short Slot)> slots)
        => gapSlots = new HashSet<(InventoryType, short)>(slots);

    public void Clear() => removableIds = new HashSet<uint>();

    public void ClearGaps() => gapSlots = new HashSet<(InventoryType, short)>();

    /// <summary>Called every frame from UiBuilder.Draw.</summary>
    public void Draw()
    {
        var drawFade = configuration.HighlightEnabled && removableIds.Count > 0;
        var drawGaps = configuration.GlamourGapsEnabled && gapSlots.Count > 0;
        if (!drawFade && !drawGaps)
            return;

        var inv = InventoryManager.Instance();
        if (inv == null)
            return;

        // The grid renders items in the player's sorted display order, not raw container order, so
        // grid slot i does NOT map to container slot i. InventorySorter.Items is the flat display
        // list (35 per page); entry g*35+i gives the real (Page, Slot) shown in grid g slot i.
        var orderModule = ItemOrderModule.Instance();
        var sorter = orderModule != null ? orderModule->InventorySorter : null;
        if (sorter == null)
            return;

        var itemCount = (int)sorter->Items.LongCount;

        var drawList = ImGui.GetBackgroundDrawList();
        var fadeColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, configuration.FadeOpacity));
        var gapTint = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.78f, 0.25f, configuration.GlamourTintOpacity));
        var gapOutline = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.82f, 0.3f, 0.95f));

        foreach (var (addonName, page) in ResolveGrids())
        {
            var addonPtr = gameGui.GetAddonByName(addonName, 1);
            if (addonPtr == nint.Zero)
                continue;

            var grid = (AddonInventoryGrid*)addonPtr.Address;
            if (grid == null || !grid->AtkUnitBase.IsVisible)
                continue;

            var scale = grid->AtkUnitBase.Scale;
            for (var i = 0; i < SlotsPerBag; i++)
            {
                var dragDrop = grid->Slots[i].Value;
                if (dragDrop == null)
                    continue;

                if (!ResolveDisplaySlot(sorter, itemCount, inv, (page * SlotsPerBag) + i,
                                        out var container, out var containerSlot, out var itemId))
                    continue;

                // Keeper = occupied slot whose item is NOT on the removable list.
                var keeper = drawFade && itemId != 0 && !removableIds.Contains(itemId);
                var gap = drawGaps && gapSlots.Contains((container, containerSlot));
                if (!keeper && !gap)
                    continue;

                var node = (AtkResNode*)dragDrop->OwnerNode;

                if (keeper)
                    SlotOverlay.DrawRect(node, scale, drawList, fadeColor);
                else
                    SlotOverlay.DrawRect(node, scale, drawList, gapTint);

                if (gap)
                    SlotOverlay.DrawBorder(node, scale, drawList, gapOutline);
            }
        }
    }

    /// <summary>
    /// Every inventory grid addon on screen, paired with the bag page it is showing. Only one
    /// layout can be open at a time, so the ones that are not simply fail the visibility check.
    /// </summary>
    private List<(string Addon, int Page)> ResolveGrids()
    {
        // Reused rather than reallocated: this runs on every frame the overlay is up, and Draw is
        // the only caller, on the one UI thread.
        var grids = resolvedGrids;
        grids.Clear();

        for (var g = 0; g < ExpandedGrids.Length; g++)
            grids.Add((ExpandedGrids[g], g));

        var largePtr = gameGui.GetAddonByName("InventoryLarge", 1);
        if (largePtr != nint.Zero)
        {
            var large = (AddonInventoryLarge*)largePtr.Address;
            if (large != null)
            {
                // One tab covers two bags: tab 0 shows bags 0 and 1, tab 1 shows bags 2 and 3.
                var first = large->TabIndex * 2;
                if (first >= 0 && first + 1 < BagCount)
                {
                    grids.Add(("InventoryGrid0", first));
                    grids.Add(("InventoryGrid1", first + 1));
                }
            }
        }

        var singlePtr = gameGui.GetAddonByName("Inventory", 1);
        if (singlePtr != nint.Zero)
        {
            var single = (AddonInventory*)singlePtr.Address;
            if (single != null && single->TabIndex >= 0 && single->TabIndex < BagCount)
                grids.Add(("InventoryGrid", single->TabIndex));
        }

        return grids;
    }

    // Maps a flat display index to the container slot shown there, via the sorter's (Page, Slot)
    // entry. The container slot is reported alongside the item id because the glamour gaps are
    // keyed by slot: two copies of one item can have different verdicts.
    private static bool ResolveDisplaySlot(ItemOrderModuleSorter* sorter, int itemCount, InventoryManager* inv,
                                           int displayIndex, out InventoryType containerType,
                                           out short containerSlot, out uint itemId)
    {
        containerType = InventoryType.Invalid;
        containerSlot = 0;
        itemId = 0;

        if (displayIndex < 0 || displayIndex >= itemCount)
            return false;

        var entry = sorter->Items[(long)displayIndex].Value;
        if (entry == null)
            return false;

        containerType = InventoryType.Inventory1 + entry->Page;
        containerSlot = (short)entry->Slot;

        var container = inv->GetInventoryContainer(containerType);
        if (container == null)
            return false;

        var slot = container->GetInventorySlot(entry->Slot);
        itemId = slot != null ? slot->ItemId : 0u;
        return true;
    }
}
