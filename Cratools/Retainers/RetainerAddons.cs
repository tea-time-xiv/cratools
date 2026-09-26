using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Cratools.Retainers;

/// <summary>
/// What the retainer addons hold, as confirmed in-game with "/cratools retainerdump" on
/// 2026-09-26. None of it has a ClientStructs struct, so the layouts live here in one place.
/// </summary>
public static unsafe class RetainerAddons
{
    public const string RetainerList = "RetainerList";
    public const string RetainerMenu = "SelectString";
    public const string SaleHistory = "RetainerHistory";
    public const string Talk = "Talk";

    // RetainerList AtkValues: v[2] is the retainer count, then one block of ten per retainer from
    // v[3], in the list's display order. Name at +0, the "active" flag at +8 (a retainer disabled by
    // the subscription's retainer limit is not active and cannot be opened).
    private const int RetainerCountIndex = 2;
    private const int RetainerBlockStart = 3;
    private const int RetainerBlockStride = 10;
    private const int RetainerActiveOffset = 8;
    private const int MaxRetainers = 10;

    // The venture-count box at the top right of RetainerList (node id 2). Dagobert draws its Auto
    // Pinch button over the text inside it, so ours sits just to its left.
    public const uint ButtonAnchorNodeId = 2;

    // The retainer menu's AtkValues: v[3] is the entry count and the entries start at v[7].
    // "View sale history." is entry 4 of 13 on an English client.
    private const int MenuCountIndex = 3;
    private const int MenuEntryStart = 7;
    private const int SaleHistoryEntryFallback = 4;
    private const string SaleHistoryEntryText = "View sale history";

    public readonly record struct RetainerRow(int Row, string Name, bool Active);

    public static List<RetainerRow> ReadRetainers(AtkUnitBase* list)
    {
        var rows = new List<RetainerRow>();
        if (list->AtkValuesCount <= RetainerCountIndex)
            return rows;

        var count = Math.Min((int)list->AtkValues[RetainerCountIndex].UInt, MaxRetainers);
        for (var row = 0; row < count; row++)
        {
            var start = RetainerBlockStart + row * RetainerBlockStride;
            if (start + RetainerBlockStride > list->AtkValuesCount)
                break;

            var name = GameInput.ReadString(list->AtkValues[start]);
            if (string.IsNullOrEmpty(name))
                break;

            var active = list->AtkValues[start + RetainerActiveOffset];
            rows.Add(new RetainerRow(row, name, active.Type == AtkValueType.Bool && active.Byte != 0));
        }

        return rows;
    }

    /// <summary>
    /// The index of "View sale history." in the retainer menu. It is matched by text so a shifted
    /// menu cannot send us into the wrong window; other client languages fall back to the index
    /// seen in-game, and the cycler's wait for <see cref="SaleHistory"/> stops the run if that is
    /// wrong.
    /// </summary>
    public static int FindSaleHistoryEntry(AtkUnitBase* menu, out bool matchedByText)
    {
        matchedByText = false;
        if (menu->AtkValuesCount <= MenuCountIndex)
            return -1;

        var count = menu->AtkValues[MenuCountIndex].Int;
        for (var entry = 0; entry < count && MenuEntryStart + entry < menu->AtkValuesCount; entry++)
        {
            var text = GameInput.ReadString(menu->AtkValues[MenuEntryStart + entry]);
            if (text != null && text.StartsWith(SaleHistoryEntryText, StringComparison.OrdinalIgnoreCase))
            {
                matchedByText = true;
                return entry;
            }
        }

        return count > SaleHistoryEntryFallback ? SaleHistoryEntryFallback : -1;
    }

    /// <summary>Opens the retainer on the given row of the list, as clicking it does.</summary>
    public static void SelectRetainer(AtkUnitBase* list, int row)
        => GameInput.FireCallback(list, GameInput.Int(2), GameInput.UInt((uint)row), GameInput.Undefined,
                                  GameInput.Undefined);

    public static void SelectMenuEntry(AtkUnitBase* menu, int entry)
        => GameInput.FireCallback(menu, GameInput.Int(entry));
}
