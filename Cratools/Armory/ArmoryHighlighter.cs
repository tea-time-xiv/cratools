using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace Cratools.Armory;

/// <summary>
/// Marks up the Armoury Chest: redundant gear tinted red, gear worth storing outlined gold.
///
/// The board shows one container at a time. Two facts, both confirmed in-game, make the mapping
/// work (see ArmoryDebug):
///
///  - AddonArmouryBoard.TabIndex indexes ItemOrderModule.ArmourySorter directly, *not* the visual
///    tab order. The tabs read MainHand, OffHand, Head, ... on screen, while the sorter table runs
///    MainHand, Head, Body, Hands, Legs, Feet, OffHand, ... — so selecting the Off Hand tab reports
///    TabIndex 6. Each sorter also names its own InventoryType, so nothing has to be hardcoded.
///  - Slots always has 50 components regardless of the container size, so iteration is clamped to
///    the sorter's item count.
///
/// A piece can be both at once — outclassed, but the only copy of an outfit the dresser has never
/// seen. Both marks are drawn: the junk fill says "you do not need to wear this", the gold outline
/// says "do not throw it away either". The gold fill is skipped where the red one already sits, so
/// the two never blend into a third colour.
///
/// Like the inventory highlighter, this is strictly read-only.
/// </summary>
public sealed unsafe class ArmoryHighlighter
{
    private readonly IGameGui gameGui;
    private readonly Configuration configuration;

    // Exact container slots, not item ids: two copies of one item can get opposite verdicts.
    private HashSet<(InventoryType Container, short Slot)> junkSlots = new();
    private HashSet<(InventoryType Container, short Slot)> gapSlots = new();

    public ArmoryHighlighter(IGameGui gameGui, Configuration configuration)
    {
        this.gameGui = gameGui;
        this.configuration = configuration;
    }

    public int JunkCount => junkSlots.Count;

    public void SetJunk(IEnumerable<(InventoryType Container, short Slot)> slots)
        => junkSlots = new HashSet<(InventoryType, short)>(slots);

    public void SetGaps(IEnumerable<(InventoryType Container, short Slot)> slots)
        => gapSlots = new HashSet<(InventoryType, short)>(slots);

    public void Clear() => junkSlots = new HashSet<(InventoryType, short)>();

    public void ClearGaps() => gapSlots = new HashSet<(InventoryType, short)>();

    /// <summary>Called every frame from UiBuilder.Draw.</summary>
    public void Draw()
    {
        var drawJunk = configuration.ArmoryHighlightEnabled && junkSlots.Count > 0;
        var drawGaps = configuration.GlamourGapsEnabled && gapSlots.Count > 0;
        if (!drawJunk && !drawGaps)
            return;

        var addonPtr = gameGui.GetAddonByName("ArmouryBoard", 1);
        if (addonPtr == nint.Zero)
            return;

        var board = (AddonArmouryBoard*)addonPtr.Address;
        if (board == null || !board->AtkUnitBase.IsVisible)
            return;

        var module = ItemOrderModule.Instance();
        var inv = InventoryManager.Instance();
        if (module == null || inv == null)
            return;

        var sorters = module->ArmourySorter;
        if (board->TabIndex < 0 || board->TabIndex >= sorters.Length)
            return;

        var sorter = sorters[board->TabIndex].Value;
        if (sorter == null)
            return;

        var drawList = ImGui.GetBackgroundDrawList();
        var tint = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.15f, 0.1f, configuration.ArmoryTintOpacity));
        var gapTint = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.78f, 0.25f, configuration.GlamourTintOpacity));
        var gapOutline = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.82f, 0.3f, 0.95f));

        var slots = board->Slots;
        var count = (int)System.Math.Min(slots.Length, sorter->Items.LongCount);

        for (var i = 0; i < count; i++)
        {
            var dragDrop = slots[i].Value;
            if (dragDrop == null)
                continue;

            var entry = sorter->Items[i].Value;
            if (entry == null)
                continue;

            var containerType = sorter->InventoryType + entry->Page;
            var container = inv->GetInventoryContainer(containerType);
            var slot = container != null ? container->GetInventorySlot(entry->Slot) : null;
            if (slot == null || slot->ItemId == 0)
                continue;

            var key = (containerType, (short)entry->Slot);
            var isJunk = drawJunk && junkSlots.Contains(key);
            var isGap = drawGaps && gapSlots.Contains(key);
            if (!isJunk && !isGap)
                continue;

            var node = (FFXIVClientStructs.FFXIV.Component.GUI.AtkResNode*)dragDrop->OwnerNode;
            var scale = board->AtkUnitBase.Scale;

            if (isJunk)
                SlotOverlay.DrawRect(node, scale, drawList, tint);
            else if (isGap)
                SlotOverlay.DrawRect(node, scale, drawList, gapTint);

            if (isGap)
                SlotOverlay.DrawBorder(node, scale, drawList, gapOutline);
        }
    }
}
