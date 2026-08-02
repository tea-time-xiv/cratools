using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Cratools.Armory;

/// <summary>One gear piece found in the armoury chest (or currently worn).</summary>
public readonly record struct ArmoryItem(
    InventoryType Container,
    short Slot,
    uint ItemId,
    bool IsHq,
    byte MateriaCount,
    uint GlamourId,
    byte Stain0,
    byte Stain1)
{
    public bool IsEquipped => Container == InventoryType.EquippedItems;

    /// <summary>Melded, glamoured or dyed gear is gear the player invested in, so it is protected.</summary>
    public bool IsCustomised => MateriaCount > 0 || GlamourId != 0 || Stain0 != 0 || Stain1 != 0;
}

/// <summary>
/// Reads the thirteen armoury containers plus the equipped set. Strictly read-only: it copies the
/// fields the classifier needs out of each <see cref="InventoryItem"/> and touches nothing else.
/// </summary>
public sealed unsafe class ArmoryScanner
{
    public static readonly InventoryType[] ArmoryContainers =
    {
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand,
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryWaist,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings,
        InventoryType.ArmorySoulCrystal,
    };

    /// <summary>Armoury contents. Pass includeEquipped to also report what the player is wearing.</summary>
    public List<ArmoryItem> Scan(bool includeEquipped = true)
    {
        var found = new List<ArmoryItem>();

        var manager = InventoryManager.Instance();
        if (manager == null)
            return found;

        foreach (var type in ArmoryContainers)
            ReadContainer(manager, type, found);

        if (includeEquipped)
            ReadContainer(manager, InventoryType.EquippedItems, found);

        return found;
    }

    private static void ReadContainer(InventoryManager* manager, InventoryType type, List<ArmoryItem> into)
    {
        var container = manager->GetInventoryContainer(type);
        if (container == null || !container->IsLoaded)
            return;

        for (var i = 0; i < container->Size; i++)
        {
            var slot = container->GetInventorySlot(i);
            if (slot == null || slot->ItemId == 0)
                continue;

            var stains = slot->Stains;

            into.Add(new ArmoryItem(
                type,
                (short)i,
                GearsetIndex.Normalize(slot->ItemId),
                slot->IsHighQuality(),
                slot->GetMateriaCount(),
                slot->GlamourId,
                stains.Length > 0 ? stains[0] : (byte)0,
                stains.Length > 1 ? stains[1] : (byte)0));
        }
    }
}
