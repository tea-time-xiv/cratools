using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace Cratools.Armory;

/// <summary>
/// The player's level in every class/job, refreshed on demand from PlayerState. A level of 0 means
/// the class is not unlocked, which is the first junk criterion: gear no class you own can wear.
/// </summary>
public sealed unsafe class JobUnlockState
{
    private readonly EquipRules rules;
    private readonly Dictionary<uint, int> levels = new();

    // Jobs whose soul crystal the player owns. See Refresh for why ownership is the only signal.
    private readonly HashSet<uint> unlockedJobs = new();

    // False when the inventory could not be read, which makes every soul crystal look missing.
    private bool crystalsKnown;

    public JobUnlockState(EquipRules rules)
    {
        this.rules = rules;
    }

    public bool IsLoaded { get; private set; }

    /// <summary>
    /// Classes below this level are treated as not played at all. Accessories are the reason this
    /// exists: nearly every job can wear them, so a single class parked at level 1 keeps every
    /// accessory you own "still the best" for that class and nothing is ever redundant.
    /// </summary>
    public int MinimumRelevantLevel { get; set; }

    public int LevelOf(uint classJobRowId) => levels.GetValueOrDefault(classJobRowId, 0);

    public bool IsUnlocked(uint classJobRowId) => LevelOf(classJobRowId) > 0;

    /// <summary>Unlocked and at or above <see cref="MinimumRelevantLevel"/>.</summary>
    public bool IsRelevant(uint classJobRowId)
        => LevelOf(classJobRowId) >= Math.Max(1, MinimumRelevantLevel);

    /// <summary>Ignores <see cref="MinimumRelevantLevel"/>; used to explain why an item was junked.</summary>
    public bool AnyUnlocked(ulong jobMask)
    {
        foreach (var (rowId, _) in rules.Jobs)
        {
            if (EquipRules.JobInMask(jobMask, rowId) && IsUnlocked(rowId))
                return true;
        }

        return false;
    }

    public bool AnyRelevant(ulong jobMask)
    {
        foreach (var (rowId, _) in rules.Jobs)
        {
            if (EquipRules.JobInMask(jobMask, rowId) && IsRelevant(rowId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The rows in the mask the player actually plays, which is not the same as the rows the mask
    /// names.
    ///
    /// A base class and its job share an ExpArrayIndex, so Marauder reports level 80 for a player
    /// who has not touched it since level 30. Asked "is anything better for Marauder?", the answer
    /// is always no, because every upgrade they own is Warrior-only — and one "no" is enough to keep
    /// a piece forever. So once the job is unlocked the class drops out; EquipRules has already put
    /// the job into every mask the class appears in, so nothing becomes unreachable.
    ///
    /// The mirror case is Scholar for a player who only unlocked Summoner: that job bit is in the
    /// mask purely because Arcanist is, and asking on its behalf would keep gear for a job the
    /// player does not have. Class-derived jobs therefore have to prove themselves with a soul
    /// crystal. Jobs with no base class need no proof — an unlevelled one reports level 0 and is
    /// already filtered out.
    /// </summary>
    public IEnumerable<uint> RelevantJobsIn(ulong jobMask)
    {
        foreach (var (rowId, _) in rules.Jobs)
        {
            if (EquipRules.JobInMask(jobMask, rowId) && IsPlayed(rowId))
                yield return rowId;
        }
    }

    /// <summary>Relevant by level, and not a row some other row already speaks for.</summary>
    public bool IsPlayed(uint classJobRowId)
    {
        if (!IsRelevant(classJobRowId))
            return false;

        if (!crystalsKnown)
            return true;

        return !IsSupersededByOwnJob(classJobRowId) && !IsUnprovenJob(classJobRowId);
    }

    /// <summary>False when the inventory could not be read, so no soul crystal answer is trustworthy.</summary>
    public bool SoulCrystalsKnown => crystalsKnown;

    /// <summary>Whether the player owns the job's soul crystal. Always false for a base class.</summary>
    public bool HasSoulCrystal(uint classJobRowId) => unlockedJobs.Contains(classJobRowId);

    /// <summary>A base class the player has already turned into a job, so it is never played.</summary>
    private bool IsSupersededByOwnJob(uint classJobRowId)
    {
        var derived = rules.JobsOfClass(classJobRowId);
        if (derived == null)
            return false;

        foreach (var jobRowId in derived)
        {
            if (unlockedJobs.Contains(jobRowId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A job that only looks available because its base class is: it inherited the class's level and,
    /// through EquipRules, the class's gear, but the player never took the job.
    /// </summary>
    private bool IsUnprovenJob(uint classJobRowId)
    {
        var crystal = rules.SoulCrystalOf(classJobRowId);
        return crystal != 0 && !unlockedJobs.Contains(classJobRowId);
    }

    public void Refresh()
    {
        levels.Clear();
        unlockedJobs.Clear();
        crystalsKnown = false;
        IsLoaded = false;

        var state = PlayerState.Instance();
        if (state == null || !state->IsLoaded)
            return;

        // ClassJobLevels is indexed by ClassJob.ExpArrayIndex, not by ClassJob.RowId. Jobs that
        // share an index with their base class (e.g. GLA/PLD) therefore report the same level,
        // which is what the game does too.
        var classJobLevels = state->ClassJobLevels;
        foreach (var (rowId, _) in rules.Jobs)
        {
            if (!rules.TryGetExpArrayIndex(rowId, out var index))
                continue;

            if (index < 0 || index >= classJobLevels.Length)
                continue;

            levels[rowId] = classJobLevels[index];
        }

        RefreshUnlockedJobs();

        IsLoaded = true;
    }

    /// <summary>
    /// Works out which jobs are actually unlocked. Levels cannot say: a class and its job share an
    /// ExpArrayIndex, so Marauder and Warrior always report the same number whether or not the job
    /// quest was ever done. Owning the soul crystal is the signal — it is handed over by that quest
    /// and is unique and untradable, so it cannot be sold off by accident.
    ///
    /// GetInventoryItemCount covers the bags, the armoury and the equipped set, which is everywhere
    /// a soul crystal can be. If the inventory is not readable the result is left unknown rather
    /// than empty, because an empty set would read as "no jobs unlocked" and quietly disable both
    /// refinements in RelevantJobsIn.
    /// </summary>
    private void RefreshUnlockedJobs()
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return;

        // The soul crystal drawer is read directly as well: GetInventoryItemCount is documented to
        // sweep the armoury, but the crystals live in a container of their own and a miss there
        // would silently mark every job unproven.
        var drawer = new HashSet<uint>();
        var container = inventory->GetInventoryContainer(InventoryType.ArmorySoulCrystal);
        if (container != null && container->IsLoaded)
        {
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot != null && slot->ItemId != 0)
                    drawer.Add(GearsetIndex.Normalize(slot->ItemId));
            }
        }

        foreach (var (rowId, _) in rules.Jobs)
        {
            var crystal = rules.SoulCrystalOf(rowId);
            if (crystal == 0)
                continue; // a base class, or a job the sheet gives no crystal

            if (drawer.Contains(crystal) || inventory->GetInventoryItemCount(crystal) > 0)
                unlockedJobs.Add(rowId);
        }

        crystalsKnown = true;
    }
}
