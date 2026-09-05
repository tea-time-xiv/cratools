using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Cratools.Armory;

public enum GlamourGapKind
{
    /// <summary>Already collected, or not collectable this way.</summary>
    None,

    /// <summary>Belongs to an outfit that is not in the dresser at all: store it as a new set.</summary>
    StoreAsNewSet,

    /// <summary>Belongs to an outfit already in the dresser whose slot for this piece is empty.</summary>
    StoreIntoExistingSet,

    /// <summary>Armoire-eligible and not deposited yet. The dresser refuses these, the armoire is free.</summary>
    StoreInArmoire,
}

/// <summary>
/// One piece worth keeping because it is not collected yet. <see cref="Blocker"/> is set when the
/// deposit cannot happen as things stand (worn, melded, damaged, bound to a gearset) — the piece is
/// still a gap, it just needs a step first, and saying so is more useful than hiding the row.
/// </summary>
public readonly record struct GlamourGapEntry(
    ArmoryItem Item,
    string Name,
    GearSlot Slot,
    GlamourGapKind Kind,
    uint SetId,
    string SetName,
    string Explanation,
    string? Blocker)
{
    public bool IsActionable => Blocker == null;
}

public sealed class GlamourGapReport
{
    public List<GlamourGapEntry> Entries { get; } = new();

    public DresserState State { get; init; } = new();

    public int Scanned { get; set; }

    public int ActionableCount { get; set; }

    public int BlockedCount { get; set; }

    /// <summary>Container slots holding a gap, for the overlay. Slots, not ids: copies differ.</summary>
    public HashSet<(InventoryType Container, short Slot)> GapSlots { get; } = new();
}

/// <summary>
/// Finds gear you hold that the glamour dresser (or the armoire) has never seen.
///
/// The rules, in the order they are applied, each one there because skipping it produces a
/// suggestion the game would refuse:
///
///  1. Armoire-eligible gear can never go in the dresser. If the armoire already holds it there is
///     nothing to do; if not, the action is an armoire deposit, not a dresser one.
///  2. A piece sitting loose in the dresser is collected, whatever its outfits say.
///  3. Otherwise every outfit containing the piece is checked. If any stored outfit already fills
///     this piece's slot, it is collected. If none does, it is a gap — into an outfit that is
///     already stored where possible, as a new outfit otherwise.
///
/// Read-only, like the rest of the plugin: it never deposits anything.
/// </summary>
public sealed class GlamourGapFinder
{
    // Fully repaired gear reads 30000; the dresser will not take damaged pieces.
    private const ushort FullCondition = 30000;

    private readonly GlamourSets sets;
    private readonly EquipRules rules;
    private readonly GearsetIndex gearsets;
    private readonly Configuration configuration;

    public GlamourGapFinder(GlamourSets sets, EquipRules rules, GearsetIndex gearsets, Configuration configuration)
    {
        this.sets = sets;
        this.rules = rules;
        this.gearsets = gearsets;
        this.configuration = configuration;
    }

    public GlamourGapReport Find(IReadOnlyList<ArmoryItem> scanned, DresserState state)
    {
        var report = new GlamourGapReport { State = state };

        // One row per distinct piece, not per copy. The dresser only ever needs one of a thing, so
        // a second copy of a piece already listed says nothing new, and counting it would inflate
        // both the totals and the per-outfit "N here". Where copies disagree, the one that can
        // actually be deposited wins over one that is worn, melded or damaged.
        var best = new Dictionary<uint, GlamourGapEntry>();
        var order = new List<uint>();

        foreach (var item in scanned)
        {
            report.Scanned++;

            var entry = Evaluate(item, state);
            if (entry.Kind == GlamourGapKind.None)
                continue;

            if (entry.Kind == GlamourGapKind.StoreInArmoire && !configuration.GlamourIncludeArmoire)
                continue;

            if (!best.TryGetValue(item.ItemId, out var existing))
            {
                best[item.ItemId] = entry;
                order.Add(item.ItemId);
                continue;
            }

            if (!existing.IsActionable && entry.IsActionable)
                best[item.ItemId] = entry;
        }

        foreach (var id in order)
        {
            var entry = best[id];
            report.Entries.Add(entry);

            if (entry.IsActionable)
            {
                report.ActionableCount++;
                report.GapSlots.Add((entry.Item.Container, entry.Item.Slot));
            }
            else
            {
                report.BlockedCount++;
            }
        }

        return report;
    }

    private GlamourGapEntry Evaluate(ArmoryItem item, DresserState state)
    {
        var id = item.ItemId;

        // An item with no facts is not gear the equip rules know; naming it after the default
        // GearSlot (MainHand) would be a lie, so it is reported as slotless instead.
        var known = rules.TryGetFacts(id, out var facts);
        var name = known ? facts.Name : $"Item #{id}";
        var slot = known ? facts.Slot : GearSlot.Unknown;

        if (sets.IsCabinetItem(id))
        {
            // The armoire is free and unlimited, so an armoire item never belongs in the dresser.
            if (!state.ArmoireKnown || state.IsInArmoire(id))
                return default;

            return new GlamourGapEntry(item, name, slot, GlamourGapKind.StoreInArmoire, 0, string.Empty,
                                       "Not in the armoire yet.", BlockerFor(item));
        }

        if (!state.DresserKnown)
            return default;

        if (!sets.TryGetMemberships(id, out var memberships))
            return default;

        if (state.IsLooseInDresser(id))
            return default;

        uint newSetId = 0;
        uint existingSetId = 0;

        foreach (var membership in memberships)
        {
            if (state.IsSetSlotFilled(membership.SetId, membership.SlotIndex))
                return default;

            if (state.IsSetStored(membership.SetId))
            {
                if (existingSetId == 0)
                    existingSetId = membership.SetId;
            }
            else if (newSetId == 0)
            {
                newSetId = membership.SetId;
            }
        }

        var blocker = BlockerFor(item);

        if (existingSetId != 0)
        {
            var setName = sets.SetName(existingSetId);
            return new GlamourGapEntry(item, name, slot, GlamourGapKind.StoreIntoExistingSet,
                                       existingSetId, setName,
                                       $"\"{setName}\" is in the dresser but this slot of it is empty.",
                                       blocker);
        }

        if (newSetId != 0)
        {
            var setName = sets.SetName(newSetId);
            var extra = memberships.Count > 1 ? $" (one of {memberships.Count} outfits it fits)" : string.Empty;
            return new GlamourGapEntry(item, name, slot, GlamourGapKind.StoreAsNewSet,
                                       newSetId, setName,
                                       $"Storable as \"{setName}\", which the dresser does not hold yet{extra}.",
                                       blocker);
        }

        return default;
    }

    /// <summary>What stands between the piece and a deposit, or null when nothing does.</summary>
    private string? BlockerFor(ArmoryItem item)
    {
        if (item.IsEquipped)
            return "worn";

        if (gearsets.Contains(item.ItemId))
            return "in a gearset";

        if (item.MateriaCount > 0)
            return "melded";

        if (item.Condition < FullCondition)
            return "needs repair";

        return null;
    }
}
