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
/// Fades (dims) the "keeper" slots in the open-all-bags inventory so the removable items pointed
/// at by the pasted list stay bright and stand out.
///
/// The item slots live in the four grid addons InventoryGrid0E–3E (not the InventoryExpansion
/// frame). The grid renders items in the player's sorted display order, so a visual slot is mapped
/// to its real item via ItemOrderModule.InventorySorter (see <see cref="ResolveDisplayItemId"/>).
///
/// Read-only: it never writes to game memory. Each frame it reads slot-node screen rects, then
/// draws on the ImGui background draw list. Nothing to reset.
/// </summary>
public sealed unsafe class InventoryHighlighter
{
    private const int SlotsPerBag = 35;

    // The four open-all-bags grid addons (the "E" suffix = expanded view). Grid g shows display
    // page g of the sorter.
    private static readonly string[] GridAddons =
    {
        "InventoryGrid0E", "InventoryGrid1E", "InventoryGrid2E", "InventoryGrid3E",
    };

    private readonly IGameGui gameGui;
    private readonly Configuration configuration;

    private HashSet<uint> removableIds = new();

    public InventoryHighlighter(IGameGui gameGui, Configuration configuration)
    {
        this.gameGui = gameGui;
        this.configuration = configuration;
    }

    public int RemovableCount => removableIds.Count;

    public void SetRemovable(IEnumerable<uint> ids) => removableIds = new HashSet<uint>(ids);

    public void Clear() => removableIds = new HashSet<uint>();

    /// <summary>Called every frame from UiBuilder.Draw.</summary>
    public void Draw()
    {
        if (!configuration.HighlightEnabled || removableIds.Count == 0)
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

        for (var g = 0; g < GridAddons.Length; g++)
        {
            var addonPtr = gameGui.GetAddonByName(GridAddons[g], 1);
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

                var itemId = ResolveDisplayItemId(sorter, itemCount, inv, (g * SlotsPerBag) + i);

                // Keeper = occupied slot whose item is NOT on the removable list.
                var keeper = itemId != 0 && !removableIds.Contains(itemId);
                if (!keeper)
                    continue;

                DrawFade((AtkResNode*)dragDrop->OwnerNode, scale, drawList, fadeColor);
            }
        }
    }

    // Maps a flat display index to the item shown there, via the sorter's (Page, Slot) entry.
    private static uint ResolveDisplayItemId(ItemOrderModuleSorter* sorter, int itemCount, InventoryManager* inv, int displayIndex)
    {
        if (displayIndex < 0 || displayIndex >= itemCount)
            return 0;

        var entry = sorter->Items[(long)displayIndex].Value;
        if (entry == null)
            return 0;

        var container = inv->GetInventoryContainer(InventoryType.Inventory1 + entry->Page);
        if (container == null)
            return 0;

        var slot = container->GetInventorySlot(entry->Slot);
        return slot != null ? slot->ItemId : 0u;
    }

    private static void DrawFade(AtkResNode* node, float scale, ImDrawListPtr drawList, uint color)
    {
        if (node == null)
            return;

        var pos = new Vector2(node->ScreenX, node->ScreenY);
        var size = new Vector2(node->Width * scale, node->Height * scale);
        drawList.AddRectFilled(pos, pos + size, color);
    }
}
