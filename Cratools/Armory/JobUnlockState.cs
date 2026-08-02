using System;
using System.Collections.Generic;
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

    public IEnumerable<uint> RelevantJobsIn(ulong jobMask)
    {
        foreach (var (rowId, _) in rules.Jobs)
        {
            if (EquipRules.JobInMask(jobMask, rowId) && IsRelevant(rowId))
                yield return rowId;
        }
    }

    public void Refresh()
    {
        levels.Clear();
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

        IsLoaded = true;
    }
}
