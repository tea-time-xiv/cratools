using Lumina.Excel.Sheets;

namespace Cratools.Armory;

/// <summary>
/// The equipment slots the armoury is organised by. Left and right ring collapse into
/// <see cref="Finger"/> because the game stores both in one armoury container.
/// </summary>
public enum GearSlot
{
    MainHand,
    OffHand,
    Head,
    Body,
    Hands,
    Waist,
    Legs,
    Feet,
    Ears,
    Neck,
    Wrists,
    Finger,
    SoulCrystal,
}

public static class GearSlots
{
    /// <summary>
    /// Maps an EquipSlotCategory row to the slot it occupies. The sheet uses one signed column per
    /// slot: 1 = occupies it, -1 = blocks it (a two-handed weapon blocks OffHand), 0 = unrelated.
    /// Only the positive column identifies the item's own slot, so the checks are ordered and
    /// test for > 0.
    /// </summary>
    public static bool TryFromEquipSlotCategory(EquipSlotCategory category, out GearSlot slot)
    {
        if (category.MainHand > 0) { slot = GearSlot.MainHand; return true; }
        if (category.OffHand > 0) { slot = GearSlot.OffHand; return true; }
        if (category.Head > 0) { slot = GearSlot.Head; return true; }
        if (category.Body > 0) { slot = GearSlot.Body; return true; }
        if (category.Gloves > 0) { slot = GearSlot.Hands; return true; }
        if (category.Waist > 0) { slot = GearSlot.Waist; return true; }
        if (category.Legs > 0) { slot = GearSlot.Legs; return true; }
        if (category.Feet > 0) { slot = GearSlot.Feet; return true; }
        if (category.Ears > 0) { slot = GearSlot.Ears; return true; }
        if (category.Neck > 0) { slot = GearSlot.Neck; return true; }
        if (category.Wrists > 0) { slot = GearSlot.Wrists; return true; }
        if (category.FingerL > 0 || category.FingerR > 0) { slot = GearSlot.Finger; return true; }
        if (category.SoulCrystal > 0) { slot = GearSlot.SoulCrystal; return true; }

        slot = default;
        return false;
    }
}
