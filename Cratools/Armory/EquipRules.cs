using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Cratools.Armory;

/// <summary>
/// Everything the junk classifier needs to know about one equippable item, precomputed from the
/// Excel sheets so the per-frame / per-scan path does no sheet lookups.
/// </summary>
public readonly record struct ItemFacts(
    uint ItemId,
    string Name,
    GearSlot Slot,
    uint ClassJobCategoryId,
    ulong JobMask,
    byte LevelEquip,
    uint ItemLevel,
    byte MainStatMask,
    byte Rarity,
    bool IsUnique);

/// <summary>
/// Static, sheet-derived rules about equippable items: which slot an item takes, which jobs may
/// wear it, the level needed, its item level and its main-attribute profile.
///
/// Built once at plugin load (same cost class as <see cref="ItemResolver"/>) and never mutated.
/// </summary>
public sealed class EquipRules
{
    // BaseParam row ids for the five main attributes. An item's main-stat profile is what makes a
    // Fending body different from a Healing body, so it is the role signal the supersede rule uses.
    private const uint ParamStrength = 1;
    private const uint ParamDexterity = 2;
    private const uint ParamVitality = 3;
    private const uint ParamIntelligence = 4;
    private const uint ParamMind = 5;

    private readonly Dictionary<uint, ItemFacts> facts = new();

    // ClassJob RowId -> in-game job level index. Used by JobUnlockState.
    private readonly Dictionary<uint, int> jobExpArrayIndex = new();
    private readonly List<(uint RowId, string Abbreviation)> jobs = new();

    // Base class RowId -> the jobs it becomes. Arcanist has two (Summoner and Scholar), so a list.
    private readonly Dictionary<uint, List<uint>> classToJobs = new();

    // Class-derived job RowId -> the soul crystal that proves the job is unlocked. Classes, and jobs
    // with no base class, have none here.
    private readonly Dictionary<uint, uint> jobSoulCrystal = new();

    public EquipRules(IDataManager dataManager, IPluginLog log)
    {
        var classJobs = dataManager.GetExcelSheet<ClassJob>();
        var items = dataManager.GetExcelSheet<Item>();
        if (classJobs == null || items == null)
        {
            log.Error("Cratools: could not open the ClassJob/Item sheets; armory rules are empty.");
            return;
        }

        foreach (var job in classJobs)
        {
            if (job.RowId == 0)
                continue; // row 0 is "adventurer", not a real class

            if (job.RowId >= 64)
            {
                // JobMask is a ulong; if Square ever ships a 64th job this needs widening.
                log.Warning($"Cratools: ClassJob row {job.RowId} exceeds the 64-bit job mask, ignored.");
                continue;
            }

            // The sheet carries placeholder rows with no abbreviation. They share an ExpArrayIndex
            // with a real class, so leaving them in makes them look unlocked and levelled.
            var abbreviation = job.Abbreviation.ExtractText();
            if (string.IsNullOrWhiteSpace(abbreviation))
                continue;

            jobs.Add((job.RowId, abbreviation));
            jobExpArrayIndex[job.RowId] = job.ExpArrayIndex;

            // A job row points at the class it grew out of; a class row points at itself. That is
            // the only place the class/job line is written down.
            var parent = job.ClassJobParent.RowId;
            if (parent != 0 && parent != job.RowId)
            {
                if (!classToJobs.TryGetValue(parent, out var derived))
                    classToJobs[parent] = derived = new List<uint>();

                derived.Add(job.RowId);

                // Only a class-derived job needs its crystal as proof. Crafter rows point at the
                // specialist "Soul of the …" items, which say nothing about whether the class is
                // played; recording those drops every non-specialist crafter from the supersede rule.
                var soulCrystal = job.ItemSoulCrystal.RowId;
                if (soulCrystal != 0)
                    jobSoulCrystal[job.RowId] = soulCrystal;
            }
        }

        var categoryJobMasks = BuildCategoryJobMasks(dataManager, log);

        foreach (var item in items)
        {
            if (item.RowId == 0)
                continue;

            var category = item.EquipSlotCategory.ValueNullable;
            if (category == null || !GearSlots.TryFromEquipSlotCategory(category.Value, out var slot))
                continue;

            var name = item.Name.ExtractText();
            if (string.IsNullOrEmpty(name))
                continue;

            var categoryId = item.ClassJobCategory.RowId;
            categoryJobMasks.TryGetValue(categoryId, out var jobMask);

            facts[item.RowId] = new ItemFacts(
                item.RowId,
                name,
                slot,
                categoryId,
                jobMask,
                item.LevelEquip,
                item.LevelItem.RowId, // the ItemLevel sheet is keyed by the item level itself
                MainStatMask(item),
                item.Rarity,
                item.IsUnique);
        }

        log.Information($"Cratools: armory rules built for {facts.Count} equippable items, {jobs.Count} jobs.");
    }

    public IReadOnlyList<(uint RowId, string Abbreviation)> Jobs => jobs;

    public bool TryGetFacts(uint itemId, out ItemFacts value) => facts.TryGetValue(itemId, out value);

    public bool TryGetExpArrayIndex(uint classJobRowId, out int index)
        => jobExpArrayIndex.TryGetValue(classJobRowId, out index);

    /// <summary>The jobs a base class becomes, or null when the row is not a base class.</summary>
    public IReadOnlyList<uint>? JobsOfClass(uint classJobRowId)
        => classToJobs.GetValueOrDefault(classJobRowId);

    /// <summary>
    /// The soul crystal item that proves a class-derived job is unlocked, or 0 for anything else.
    /// </summary>
    public uint SoulCrystalOf(uint classJobRowId) => jobSoulCrystal.GetValueOrDefault(classJobRowId, 0u);

    public static bool JobInMask(ulong mask, uint classJobRowId)
        => classJobRowId < 64 && (mask & (1UL << (int)classJobRowId)) != 0;

    /// <summary>
    /// ClassJobCategory has one bool column per class/job, laid out in ClassJob row order after the
    /// Name column, so job J lives at column J + 1. The generated Lumina struct exposes them only as
    /// named properties (ADV, GLA, ...), which cannot be indexed by job id, so the raw row is read
    /// instead.
    /// </summary>
    private Dictionary<uint, ulong> BuildCategoryJobMasks(IDataManager dataManager, IPluginLog log)
    {
        var masks = new Dictionary<uint, ulong>();

        ExcelSheet<RawRow>? raw = null;
        try
        {
            raw = dataManager.Excel.GetSheet<RawRow>(null, "ClassJobCategory");
        }
        catch (Exception ex)
        {
            log.Error(ex, "Cratools: could not open the raw ClassJobCategory sheet.");
        }

        if (raw == null)
            return masks;

        foreach (var row in raw)
        {
            var mask = 0UL;
            foreach (var (rowId, _) in jobs)
            {
                if (row.ReadBoolColumn((int)rowId + 1))
                    mask |= 1UL << (int)rowId;
            }

            // A job wears everything its base class can, but ClassJobCategory only ever names the
            // class. Without this, job-only gear never competes with class gear and every axe an
            // "MRD WAR" category covers looks like the Marauder's last resort.
            foreach (var (classRow, derived) in classToJobs)
            {
                if ((mask & (1UL << (int)classRow)) == 0)
                    continue;

                foreach (var jobRow in derived)
                    mask |= 1UL << (int)jobRow;
            }

            masks[row.RowId] = mask;
        }

        return masks;
    }

    // Bit 0..4 = STR, DEX, VIT, INT, MND present on the item with a non-zero value.
    private static byte MainStatMask(Item item)
    {
        byte mask = 0;
        for (var i = 0; i < item.BaseParam.Count; i++)
        {
            if (item.BaseParamValue[i] == 0)
                continue;

            mask |= item.BaseParam[i].RowId switch
            {
                ParamStrength => 1 << 0,
                ParamDexterity => 1 << 1,
                ParamVitality => 1 << 2,
                ParamIntelligence => 1 << 3,
                ParamMind => 1 << 4,
                _ => 0,
            };
        }

        return mask;
    }
}
