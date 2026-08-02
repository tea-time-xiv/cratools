using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace Cratools.Armory;

/// <summary>
/// Every item id referenced by any saved gearset. This is the hard protection rule: whatever the
/// classifier decides, a gearset item is never junk.
/// </summary>
public sealed unsafe class GearsetIndex
{
    // Gearsets store HQ items as base id + this offset.
    private const uint HqOffset = 1_000_000;

    private HashSet<uint> itemIds = new();

    public int GearsetCount { get; private set; }

    public int ItemCount => itemIds.Count;

    public bool Contains(uint itemId) => itemIds.Contains(Normalize(itemId));

    public static uint Normalize(uint itemId) => itemId > HqOffset ? itemId - HqOffset : itemId;

    public void Refresh()
    {
        var ids = new HashSet<uint>();
        var count = 0;

        var module = RaptureGearsetModule.Instance();
        if (module != null)
        {
            foreach (ref var entry in module->Entries)
            {
                if (!entry.Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
                    continue;

                count++;

                // Items covers every equipment slot of the set. Slots the set leaves empty read 0.
                foreach (ref var item in entry.Items)
                {
                    if (item.ItemId != 0)
                        ids.Add(Normalize(item.ItemId));
                }
            }
        }

        itemIds = ids;
        GearsetCount = count;
    }
}
