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

    public ArmoryDebug(IGameGui gameGui, IPluginLog log, EquipRules rules, ArmoryScanner scanner,
                       GearsetIndex gearsets, JobUnlockState jobs)
    {
        this.gameGui = gameGui;
        this.log = log;
        this.rules = rules;
        this.scanner = scanner;
        this.gearsets = gearsets;
        this.jobs = jobs;
    }

    public void Dump()
    {
        DumpContainers();
        DumpGearsets();
        DumpJobs();
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

        log.Information($"=== Cratools armory: jobs ===\n  unlocked: {unlocked}\n  locked: {locked}");
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
