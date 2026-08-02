using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Cratools.Armory;

public enum VerdictKind
{
    /// <summary>Worth keeping, or protected by a rule.</summary>
    Keep,

    /// <summary>No class or job the player has unlocked can equip it.</summary>
    JunkLockedJob,

    /// <summary>Better gear is already owned for every unlocked job that could wear it.</summary>
    JunkSuperseded,

    /// <summary>A spare copy of a piece that is already being kept.</summary>
    JunkDuplicate,
}

/// <summary>Why an item was kept. Protections win over every junk rule.</summary>
public enum KeepReason
{
    NoBetterOwned,
    Gearset,
    Equipped,
    Customised,
    Unique,
    KeepList,
}

public readonly record struct ArmoryVerdict(
    ArmoryItem Item,
    ItemFacts Facts,
    VerdictKind Kind,
    KeepReason Keep,
    string Explanation)
{
    public bool IsJunk => Kind != VerdictKind.Keep;
}

public sealed class ArmoryReport
{
    public List<ArmoryVerdict> Verdicts { get; } = new();
    public int JunkCount { get; set; }
    public int LockedJobCount { get; set; }
    public int SupersededCount { get; set; }
    public int DuplicateCount { get; set; }
    public int ProtectedCount { get; set; }
    public bool PlayerStateLoaded { get; set; }

    /// <summary>
    /// The exact container slots judged junk, for the overlay to tint. Slots rather than item ids:
    /// duplicates mean two copies of one id can get opposite verdicts.
    /// </summary>
    public HashSet<(InventoryType Container, short Slot)> JunkSlots { get; } = new();
}

/// <summary>
/// Decides which armoury gear is redundant.
///
/// Two junk rules, applied only after every protection has been checked:
///
///  1. Locked job: no class or job the player actually plays can equip the item at all. This is what
///     makes the feature worthwhile for weapons, where each one serves a single class.
///  2. Superseded: for *every* played job that could wear it, the player already owns something
///     better. "Better" is deliberately narrow — a higher item level is not enough on its own, the
///     candidate must also be wearable at that job's current level and share the item's role, so
///     an ilvl 130 tank helm never supersedes an ilvl 120 healer helm.
///
/// "Plays" means unlocked and at or above Configuration.ArmoryIgnoreJobsBelowLevel; see
/// <see cref="JobUnlockState.MinimumRelevantLevel"/> for why that knob has to exist.
///
/// Pure computation over scanned data: no game memory is written and no UI is touched.
/// </summary>
public sealed class ArmoryAnalyzer
{
    private readonly EquipRules rules;
    private readonly JobUnlockState jobs;
    private readonly GearsetIndex gearsets;
    private readonly Configuration configuration;

    public ArmoryAnalyzer(EquipRules rules, JobUnlockState jobs, GearsetIndex gearsets, Configuration configuration)
    {
        this.rules = rules;
        this.jobs = jobs;
        this.gearsets = gearsets;
        this.configuration = configuration;
    }

    public ArmoryReport Analyze(IReadOnlyList<ArmoryItem> scanned)
    {
        var report = new ArmoryReport { PlayerStateLoaded = jobs.IsLoaded };

        // Candidates for "you already own something better" are everything in the armoury plus
        // what is currently worn, grouped by the slot they compete for.
        var bySlot = new Dictionary<GearSlot, List<(ArmoryItem Item, ItemFacts Facts)>>();
        var resolved = new List<(ArmoryItem Item, ItemFacts Facts)>(scanned.Count);

        foreach (var item in scanned)
        {
            if (!rules.TryGetFacts(item.ItemId, out var facts))
                continue;

            resolved.Add((item, facts));

            if (!bySlot.TryGetValue(facts.Slot, out var list))
                bySlot[facts.Slot] = list = new List<(ArmoryItem, ItemFacts)>();

            list.Add((item, facts));
        }

        foreach (var (item, facts) in resolved)
        {
            // Equipped gear is listed only so it can supersede armoury spares; it is never judged.
            if (item.IsEquipped)
                continue;

            report.Verdicts.Add(Judge(item, facts, bySlot));
        }

        MarkDuplicates(report);
        Tally(report);

        return report;
    }

    /// <summary>
    /// Turns spare copies of a survivor into junk: if two identical pieces both came out of the
    /// rules worth keeping, only one of them is.
    ///
    /// Deliberately narrow. It only looks at pieces kept on their own merit — a protected copy
    /// (gearset, melded, pinned) neither becomes a duplicate nor makes others one, because a
    /// gearset stores item ids rather than slots and cannot tell two copies apart. Rings are
    /// skipped entirely: both hands take one, so a second copy is a legitimate pair.
    /// </summary>
    private static void MarkDuplicates(ArmoryReport report)
    {
        var survivors = new Dictionary<uint, List<int>>();

        for (var i = 0; i < report.Verdicts.Count; i++)
        {
            var verdict = report.Verdicts[i];
            if (verdict.IsJunk || verdict.Keep != KeepReason.NoBetterOwned)
                continue;

            if (verdict.Facts.Slot == GearSlot.Finger)
                continue;

            if (!survivors.TryGetValue(verdict.Item.ItemId, out var indices))
                survivors[verdict.Item.ItemId] = indices = new List<int>();

            indices.Add(i);
        }

        foreach (var (_, indices) in survivors)
        {
            if (indices.Count < 2)
                continue;

            // Keep the best copy: high quality wins, then the earliest slot, so the survivor is
            // stable between scans.
            var keeper = indices[0];
            foreach (var index in indices)
            {
                var candidate = report.Verdicts[index].Item;
                var current = report.Verdicts[keeper].Item;
                if (candidate.IsHq && !current.IsHq)
                    keeper = index;
            }

            foreach (var index in indices)
            {
                if (index == keeper)
                    continue;

                var verdict = report.Verdicts[index];
                report.Verdicts[index] = verdict with
                {
                    Kind = VerdictKind.JunkDuplicate,
                    Explanation = $"Spare copy; you own {indices.Count}.",
                };
            }
        }
    }

    private static void Tally(ArmoryReport report)
    {
        foreach (var verdict in report.Verdicts)
        {
            switch (verdict.Kind)
            {
                case VerdictKind.JunkLockedJob:
                    report.LockedJobCount++;
                    break;
                case VerdictKind.JunkSuperseded:
                    report.SupersededCount++;
                    break;
                case VerdictKind.JunkDuplicate:
                    report.DuplicateCount++;
                    break;
            }

            if (verdict.IsJunk)
            {
                report.JunkCount++;
                report.JunkSlots.Add((verdict.Item.Container, verdict.Item.Slot));
            }
            else if (verdict.Keep != KeepReason.NoBetterOwned)
            {
                report.ProtectedCount++;
            }
        }
    }

    private ArmoryVerdict Judge(ArmoryItem item, ItemFacts facts,
                                Dictionary<GearSlot, List<(ArmoryItem Item, ItemFacts Facts)>> bySlot)
    {
        if (configuration.ArmoryKeepList.Contains(item.ItemId))
            return Kept(item, facts, KeepReason.KeepList, "On your keep list.");

        if (gearsets.Contains(item.ItemId))
            return Kept(item, facts, KeepReason.Gearset, "Used by a gearset.");

        if (configuration.ArmoryProtectCustomised && item.IsCustomised)
            return Kept(item, facts, KeepReason.Customised, Customisation(item));

        if (configuration.ArmoryProtectUnique && (facts.IsUnique || facts.Rarity >= 3))
            return Kept(item, facts, KeepReason.Unique, facts.IsUnique ? "Unique item." : "Rare item.");

        // Rule 1: nothing you play can wear it.
        if (!jobs.AnyRelevant(facts.JobMask))
        {
            var threshold = configuration.ArmoryIgnoreJobsBelowLevel;
            var reason = threshold > 1 && jobs.AnyUnlocked(facts.JobMask)
                ? $"Only classes below level {threshold} can equip this."
                : "No unlocked class or job can equip this.";

            return new ArmoryVerdict(item, facts, VerdictKind.JunkLockedJob, KeepReason.NoBetterOwned, reason);
        }

        // Rule 2: superseded for every job you actually play that could wear it.
        var candidates = bySlot.GetValueOrDefault(facts.Slot);
        if (candidates == null)
            return Kept(item, facts, KeepReason.NoBetterOwned, "Nothing better owned.");

        ItemFacts? weakestUpgrade = null;
        foreach (var jobId in jobs.RelevantJobsIn(facts.JobMask))
        {
            if (!TryFindUpgrade(item, facts, jobId, candidates, out var upgrade))
            {
                var abbreviation = AbbreviationOf(jobId);
                return Kept(item, facts, KeepReason.NoBetterOwned,
                            $"Still your best for {abbreviation} (lv {jobs.LevelOf(jobId)}).");
            }

            // Report the least impressive upgrade: it is the one that justifies the whole verdict.
            if (weakestUpgrade == null || upgrade.ItemLevel < weakestUpgrade.Value.ItemLevel)
                weakestUpgrade = upgrade;
        }

        var better = weakestUpgrade!.Value;
        return new ArmoryVerdict(item, facts, VerdictKind.JunkSuperseded, KeepReason.NoBetterOwned,
                                 $"Superseded by {better.Name} (i{better.ItemLevel} vs i{facts.ItemLevel}).");
    }

    /// <summary>
    /// Looks for gear the given job would rather wear than <paramref name="facts"/>: same slot,
    /// usable by that job, already wearable at that job's level, higher item level, and the same
    /// role.
    /// </summary>
    private bool TryFindUpgrade(ArmoryItem item, ItemFacts facts, uint jobId,
                                List<(ArmoryItem Item, ItemFacts Facts)> candidates, out ItemFacts upgrade)
    {
        upgrade = default;
        var found = false;
        var jobLevel = jobs.LevelOf(jobId);

        foreach (var (otherItem, other) in candidates)
        {
            // Skip this very piece; a second copy of the same item is not an upgrade either.
            if (otherItem.Container == item.Container && otherItem.Slot == item.Slot)
                continue;

            if (!EquipRules.JobInMask(other.JobMask, jobId))
                continue;

            if (other.LevelEquip > jobLevel)
                continue; // owned, but this job cannot wear it yet

            if (other.ItemLevel <= facts.ItemLevel)
                continue;

            if (!SameRole(facts, other))
                continue;

            if (!found || other.ItemLevel < upgrade.ItemLevel)
            {
                upgrade = other;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// Gear of the same slot and job can still serve different roles — a Fending body and a Healing
    /// body are both worn by plenty of shared low-level categories. Identical ClassJobCategory rows
    /// mean the same role by definition; otherwise the main-attribute profile (STR/DEX/VIT/INT/MND)
    /// has to match, which is what actually distinguishes the roles.
    /// </summary>
    private static bool SameRole(ItemFacts item, ItemFacts candidate)
    {
        if (item.ClassJobCategoryId == candidate.ClassJobCategoryId)
            return true;

        return item.MainStatMask != 0 && item.MainStatMask == candidate.MainStatMask;
    }

    private string AbbreviationOf(uint jobId)
    {
        foreach (var (rowId, abbreviation) in rules.Jobs)
        {
            if (rowId == jobId)
                return abbreviation;
        }

        return $"job {jobId}";
    }

    private static string Customisation(ArmoryItem item)
    {
        if (item.MateriaCount > 0)
            return $"Melded with {item.MateriaCount} materia.";

        return item.GlamourId != 0 ? "Has a glamour applied." : "Dyed.";
    }

    private static ArmoryVerdict Kept(ArmoryItem item, ItemFacts facts, KeepReason reason, string explanation)
        => new(item, facts, VerdictKind.Keep, reason, explanation);
}
