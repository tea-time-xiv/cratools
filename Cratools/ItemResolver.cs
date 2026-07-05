using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace Cratools;

/// <summary>
/// Resolves item names (as printed by Teamcraft) to their in-game item RowId, using the Lumina
/// Item sheet. Names are matched case-insensitively. HQ items share the base item's RowId, so a
/// base-name match also covers the HQ variant.
/// </summary>
public sealed class ItemResolver
{
    private readonly Dictionary<string, uint> nameToId;

    public ItemResolver(IDataManager dataManager)
    {
        nameToId = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        var sheet = dataManager.GetExcelSheet<Item>();
        if (sheet == null)
            return;

        foreach (var item in sheet)
        {
            var name = item.Name.ExtractText();
            if (string.IsNullOrEmpty(name))
                continue;

            // First writer wins to keep the lowest RowId for duplicate names.
            nameToId.TryAdd(name, item.RowId);
        }
    }

    public bool TryResolve(string name, out uint itemId)
        => nameToId.TryGetValue(name.Trim(), out itemId);
}
