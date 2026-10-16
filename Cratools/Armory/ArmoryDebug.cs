using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Cratools.Armory;

/// <summary>
/// Diagnostics for the armoury feature, run with "/cratools armorydump".
///
/// Its job is to pin down the two things the overlay needs and that only the running game can
/// confirm: how many slot components AddonArmouryBoard exposes, and which armoury container the
/// visible tab is showing. It prints the sorter table alongside the slot-by-slot item names of the
/// tab currently on screen, so the mapping can be checked by eye against the window.
///
/// Read-only, like the rest of the plugin.
/// </summary>
public sealed unsafe class ArmoryDebug
{
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly EquipRules rules;
    private readonly ArmoryScanner scanner;
    private readonly GearsetIndex gearsets;
    private readonly JobUnlockState jobs;
    private readonly ArmoryAnalyzer analyzer;
    private readonly Configuration configuration;

    /// <summary>How many individual junk rows the verdict section prints before it stops.</summary>
    private const int SampleRows = 40;

    public ArmoryDebug(IGameGui gameGui, IPluginLog log, EquipRules rules, ArmoryScanner scanner,
                       GearsetIndex gearsets, JobUnlockState jobs, ArmoryAnalyzer analyzer,
                       Configuration configuration)
    {
        this.gameGui = gameGui;
        this.log = log;
        this.rules = rules;
        this.scanner = scanner;
        this.gearsets = gearsets;
        this.jobs = jobs;
        this.analyzer = analyzer;
        this.configuration = configuration;
    }

    public void Dump()
    {
        DumpContainers();
        DumpGearsets();
        DumpJobs();
        DumpVerdicts();
        DumpSorters();
        DumpBoard();
    }

    private void DumpContainers()
    {
        var items = scanner.Scan();
        var manager = InventoryManager.Instance();

        log.Information("=== Cratools armory: containers ===");
        foreach (var type in ArmoryScanner.ArmoryContainers)
        {
            var container = manager != null ? manager->GetInventoryContainer(type) : null;
            var size = container != null ? container->Size : -1;
            var used = 0;
            foreach (var item in items)
            {
                if (item.Container == type)
                    used++;
            }

            log.Information($"  {type} ({(int)type}): {used}/{size} used");
        }

        var equipped = 0;
        var unknown = 0;
        foreach (var item in items)
        {
            if (item.IsEquipped)
                equipped++;
            if (!rules.TryGetFacts(item.ItemId, out _))
                unknown++;
        }

        log.Information($"  equipped: {equipped}; total scanned: {items.Count}; without sheet facts: {unknown}");
    }

    private void DumpGearsets()
    {
        gearsets.Refresh();
        log.Information($"=== Cratools armory: gearsets === {gearsets.GearsetCount} sets, " +
                        $"{gearsets.ItemCount} distinct item ids protected");
    }

    private void DumpJobs()
    {
        jobs.Refresh();
        if (!jobs.IsLoaded)
        {
            log.Warning("=== Cratools armory: jobs === PlayerState not loaded");
            return;
        }

        var unlocked = new StringBuilder();
        var locked = new StringBuilder();
        foreach (var (rowId, abbreviation) in rules.Jobs)
        {
            var target = jobs.IsUnlocked(rowId) ? unlocked : locked;
            if (target.Length > 0)
                target.Append(", ");
            target.Append(abbreviation);
            if (jobs.IsUnlocked(rowId))
                target.Append(' ').Append(jobs.LevelOf(rowId));
        }

        // Which rows the supersede rule will actually speak for. A base class whose job is unlocked
        // and a job the player never took both drop out here, and getting that wrong is invisible in
        // the verdicts — it just looks like gear is never superseded.
        var played = new StringBuilder();
        var ignored = new StringBuilder();
        foreach (var (rowId, abbreviation) in rules.Jobs)
        {
            if (!jobs.IsUnlocked(rowId))
                continue;

            var target = jobs.IsPlayed(rowId) ? played : ignored;
            if (target.Length > 0)
                target.Append(", ");
            target.Append(abbreviation);
            if (jobs.HasSoulCrystal(rowId))
                target.Append(" [crystal]");
        }

        var crystals = jobs.SoulCrystalsKnown ? "read" : "UNREADABLE, filtering off";
        log.Information($"=== Cratools armory: jobs ===\n  unlocked: {unlocked}\n  locked: {locked}" +
                        $"\n  soul crystals: {crystals}\n  played: {played}\n  not played: {ignored}");
    }

    /// <summary>
    /// What the classifier actually decided, which nothing else logs — the tab renders it but
    /// leaves no record. Prints the verdict and keep-reason histograms, a per-container split, and
    /// a counterfactual run with gearset protection removed.
    ///
    /// That last part is the point of the section. Gearset membership is an unconditional keep
    /// (<see cref="ArmoryAnalyzer"/> checks it before either junk rule), so on a character with
    /// many saved sets the junk list can be near-empty while most of the armoury is in fact
    /// outdated. The counterfactual measures how much of the list the gearsets are hiding, and so
    /// how much a prune is worth, without touching a single gearset to find out.
    /// </summary>
    private void DumpVerdicts()
    {
        var scanned = scanner.Scan();
        var report = analyzer.Analyze(scanned);

        log.Information($"=== Cratools armory: verdicts === scanned={scanned.Count}, " +
                        $"judged={report.Verdicts.Count}, junk={report.JunkCount}, " +
                        $"protected={report.ProtectedCount}, playerState={report.PlayerStateLoaded}");

        log.Information($"  by kind: lockedJob={report.LockedJobCount}, " +
                        $"superseded={report.SupersededCount}, duplicate={report.DuplicateCount}, " +
                        $"keep={report.Verdicts.Count - report.JunkCount}");

        var reasons = report.Verdicts
                            .Where(v => !v.IsJunk)
                            .GroupBy(v => v.Keep)
                            .OrderByDescending(g => g.Count());
        var reasonLine = new StringBuilder();
        foreach (var group in reasons)
        {
            if (reasonLine.Length > 0)
                reasonLine.Append(", ");
            reasonLine.Append(group.Key).Append('=').Append(group.Count());
        }

        log.Information($"  keep reasons: {(reasonLine.Length > 0 ? reasonLine.ToString() : "none")}");

        // Per container, because the pain is never spread evenly: the five armour containers cap
        // at 35 and everything else has room.
        foreach (var group in report.Verdicts.GroupBy(v => v.Item.Container)
                                    .OrderBy(g => g.Key.ToString()))
        {
            var junk = group.Count(v => v.IsJunk);
            var gearset = group.Count(v => !v.IsJunk && v.Keep == KeepReason.Gearset);
            log.Information($"  {group.Key,-18} judged={group.Count(),3} junk={junk,3} " +
                            $"gearsetKept={gearset,3}");
        }

        DumpGearsetCounterfactual(scanned, report);
        DumpConsolidationSweep(scanned);

        var shown = 0;
        foreach (var verdict in report.Verdicts.Where(v => v.IsJunk))
        {
            if (shown++ >= SampleRows)
                break;

            log.Information($"  {verdict.Kind,-16} {verdict.Facts.Name} " +
                            $"[{verdict.Item.Container}:{verdict.Item.Slot}] " +
                            $"(i{verdict.Facts.ItemLevel}, lv{verdict.Facts.LevelEquip}) " +
                            $"{verdict.Explanation}");
        }

        if (report.JunkCount > SampleRows)
            log.Information($"  ... {report.JunkCount - SampleRows} more junk row(s) not listed.");
    }

    /// <summary>
    /// Prices the consolidation option (<see cref="Configuration.ArmoryConsolidateShared"/>) across
    /// a range of tolerances without changing the player's settings.
    ///
    /// The tolerance is the whole decision: it is how many item levels of stats you will give up to
    /// get a slot back. At 0 the rule is inert; wide open it will happily replace a full set of
    /// current role gear with one level 1 all-classes piece. Printing the curve is the only honest
    /// way to pick a number, since the answer depends entirely on which shared gear you happen to
    /// own.
    /// </summary>
    private void DumpConsolidationSweep(IReadOnlyList<ArmoryItem> scanned)
    {
        log.Information($"  --- if broader-class gear could supersede (tolerance sweep, " +
                        $"current setting: shared={configuration.ArmoryConsolidateShared}, " +
                        $"tolerance={configuration.ArmoryConsolidateIlvlTolerance}) ---");

        // Deltas are against the strict rule, never against the live setting: once the option is
        // on, comparing to it prints negative "gains" for every tolerance below the one in use.
        var baseline = Probe(shared: false, 0).JunkCount;

        foreach (var tolerance in new[] { 0, 15, 30, 60, 120, 999 })
        {
            var report = Probe(shared: true, tolerance);

            // Only the five capped armour containers matter; the rest have headroom.
            var capped = report.Verdicts.Count(v => v.IsJunk && IsCappedArmour(v.Item.Container));

            log.Information($"  tolerance {tolerance,3}: junk={report.JunkCount,3} " +
                            $"(+{report.JunkCount - baseline,3} over strict), " +
                            $"of which in full armour containers={capped,3}");
        }

        ArmoryReport Probe(bool shared, int tolerance)
        {
            var probe = new Configuration
            {
                ArmoryKeepList = configuration.ArmoryKeepList,
                ArmoryProtectCustomised = configuration.ArmoryProtectCustomised,
                ArmoryProtectUnique = configuration.ArmoryProtectUnique,
                ArmoryIgnoreJobsBelowLevel = configuration.ArmoryIgnoreJobsBelowLevel,
                ArmoryConsolidateShared = shared,
                ArmoryConsolidateIlvlTolerance = tolerance,
            };

            return new ArmoryAnalyzer(rules, jobs, gearsets, probe).Analyze(scanned);
        }
    }

    /// <summary>The five containers that sit at 35/35 and are the only ones worth freeing.</summary>
    private static bool IsCappedArmour(InventoryType container) => container is InventoryType.ArmoryHead
        or InventoryType.ArmoryBody or InventoryType.ArmoryHands or InventoryType.ArmoryLegs
        or InventoryType.ArmoryFeets;

    /// <summary>
    /// Re-runs the classifier against an empty <see cref="GearsetIndex"/> — never refreshed, so
    /// every lookup misses and no item is gearset-protected. Everything else is held identical,
    /// so the difference is attributable to the gearsets alone.
    /// </summary>
    private void DumpGearsetCounterfactual(IReadOnlyList<ArmoryItem> scanned, ArmoryReport actual)
    {
        var withoutGearsets = new ArmoryAnalyzer(rules, jobs, new GearsetIndex(), configuration);
        var report = withoutGearsets.Analyze(scanned);

        log.Information($"  --- if gearsets did not protect ({gearsets.GearsetCount} sets, " +
                        $"{gearsets.ItemCount} ids) ---");
        log.Information($"  junk would be {report.JunkCount} " +
                        $"(+{report.JunkCount - actual.JunkCount}): " +
                        $"lockedJob={report.LockedJobCount}, superseded={report.SupersededCount}, " +
                        $"duplicate={report.DuplicateCount}");

        // Which containers a prune would actually free, one line each, biggest win first.
        var freed = report.Verdicts
                          .Where(v => v.IsJunk)
                          .Select(v => (v.Item.Container, v.Item.Slot))
                          .Except(actual.JunkSlots)
                          .GroupBy(slot => slot.Container)
                          .OrderByDescending(g => g.Count());

        foreach (var group in freed)
            log.Information($"  would free in {group.Key}: {group.Count()}");
    }

    private void DumpSorters()
    {
        log.Information("=== Cratools armory: sorters ===");

        var module = ItemOrderModule.Instance();
        if (module == null)
        {
            log.Warning("  ItemOrderModule is null");
            return;
        }

        var sorters = module->ArmourySorter;
        log.Information($"  ArmourySorter entries: {sorters.Length}");
        for (var i = 0; i < sorters.Length; i++)
        {
            var sorter = sorters[i].Value;
            if (sorter == null)
            {
                log.Information($"  [{i}] null");
                continue;
            }

            // The first item of each sorter is a fingerprint for the tab, so the visible tab can be
            // identified even if TabIndex does not index this table.
            log.Information($"  [{i}] {sorter->InventoryType} items={sorter->Items.LongCount} " +
                            $"perPage={sorter->ItemsPerPage} first={FirstItemName(sorter)}");
        }

        // ArmoryWaist has no sorter and no tab: belts were removed in 6.0. The container survives
        // and is always empty, so the overlay simply has nothing to draw for it.
        log.Information("  (ArmoryWaist has no sorter; belts are retired)");
    }

    private string FirstItemName(ItemOrderModuleSorter* sorter)
    {
        var manager = InventoryManager.Instance();
        if (manager == null)
            return "?";

        for (long i = 0; i < sorter->Items.LongCount; i++)
        {
            var entry = sorter->Items[i].Value;
            if (entry == null)
                continue;

            var container = manager->GetInventoryContainer(sorter->InventoryType + entry->Page);
            var slot = container != null ? container->GetInventorySlot(entry->Slot) : null;
            if (slot == null || slot->ItemId == 0)
                continue;

            var itemId = GearsetIndex.Normalize(slot->ItemId);
            return rules.TryGetFacts(itemId, out var facts) ? facts.Name : $"#{itemId}";
        }

        return "(empty)";
    }

    private void DumpBoard()
    {
        log.Information("=== Cratools armory: ArmouryBoard ===");

        var addonPtr = gameGui.GetAddonByName("ArmouryBoard", 1);
        if (addonPtr == nint.Zero)
        {
            log.Information("  addon not open");
            return;
        }

        var board = (AddonArmouryBoard*)addonPtr.Address;
        log.Information($"  visible={board->AtkUnitBase.IsVisible} tabIndex={board->TabIndex} " +
                        $"slots={board->Slots.Length} scale={board->AtkUnitBase.Scale}");

        var module = ItemOrderModule.Instance();
        var manager = InventoryManager.Instance();
        if (module == null || manager == null)
            return;

        // Hypothesis under test: ArmourySorter[TabIndex] is the sorter for the tab on screen, and
        // its Items list is in the same order as Slots. If the names below match the window, the
        // overlay can use exactly this mapping.
        var sorters = module->ArmourySorter;
        if (board->TabIndex < 0 || board->TabIndex >= sorters.Length)
        {
            log.Warning($"  tabIndex {board->TabIndex} is outside the sorter table");
            return;
        }

        var sorter = sorters[board->TabIndex].Value;
        if (sorter == null)
        {
            log.Warning("  sorter for the visible tab is null");
            return;
        }

        log.Information($"  visible tab sorter: {sorter->InventoryType}, {sorter->Items.LongCount} entries");

        var shown = board->Slots.Length;
        for (var i = 0; i < shown; i++)
        {
            var name = "-";
            if (i < sorter->Items.LongCount)
            {
                var entry = sorter->Items[i].Value;
                if (entry != null)
                {
                    var container = manager->GetInventoryContainer(sorter->InventoryType + entry->Page);
                    var slot = container != null ? container->GetInventorySlot(entry->Slot) : null;
                    if (slot != null && slot->ItemId != 0)
                    {
                        var itemId = GearsetIndex.Normalize(slot->ItemId);
                        name = rules.TryGetFacts(itemId, out var facts)
                            ? $"{facts.Name} (i{facts.ItemLevel}, lv{facts.LevelEquip})"
                            : $"#{itemId}";
                    }
                    else
                    {
                        name = "(empty)";
                    }

                    name = $"page {entry->Page} slot {entry->Slot}: {name}";
                }
            }

            // Screen geometry of the slot component the overlay would paint. If the grid is split
            // into panes, the X ranges show it here, and a null owner node explains a slot that
            // never tints.
            var rect = "no component";
            var dragDrop = board->Slots[i].Value;
            if (dragDrop != null)
            {
                var node = (AtkResNode*)dragDrop->OwnerNode;
                rect = node == null
                    ? "no owner node"
                    : $"x={node->ScreenX:0} y={node->ScreenY:0} w={node->Width} h={node->Height} " +
                      $"vis={node->IsVisible()}";
            }

            log.Information($"  slot[{i,2}] [{rect}] {name}");
        }
    }
}
