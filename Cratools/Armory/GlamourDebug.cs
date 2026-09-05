using System.Linq;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace Cratools.Armory;

/// <summary>
/// Diagnostics for the glamour-collection feature, run with "/cratools glamourdump".
///
/// It exists to settle what only the running game can confirm. As of the 2026-09-05 run:
///
///  1. Settled: a stored outfit occupies its dresser entry as the set *token* id, not as its
///     pieces, and both spans are eight hundred entries long and parallel.
///  2. Settled: <see cref="ItemFinderModule.IsGlamourDresserCached"/> is not a "has data" flag —
///     it read false over eight hundred fully populated entries — so nothing gates on it.
///  3. Settled: a set bit in <c>GlamourDresserItemSetUnlockBits</c> marks a *missing* slot, not a
///     filled one — matching <see cref="MirageManager.IsSetSlotUnlocked"/> 8/8 against 0/8 for the
///     opposite reading. See <see cref="DresserState.SetBitsMeanMissingSlots"/>.
///
/// The polarity cross-check it prints only runs while the glamour dresser is open, since the prism
/// box is empty otherwise. It is kept as the regression check for a future patch changing any of
/// this.
///
/// Read-only, like the rest of the plugin.
/// </summary>
public sealed unsafe class GlamourDebug
{
    private const int SampleRows = 40;

    private static readonly string[] GridAddons =
    {
        "InventoryGrid", "InventoryGrid0", "InventoryGrid1",
        "InventoryGrid0E", "InventoryGrid1E", "InventoryGrid2E", "InventoryGrid3E",
    };

    private readonly IPluginLog log;
    private readonly IGameGui gameGui;
    private readonly GlamourSets sets;
    private readonly ArmoryScanner scanner;
    private readonly GlamourGapFinder finder;

    public GlamourDebug(IPluginLog log, IGameGui gameGui, GlamourSets sets, ArmoryScanner scanner,
                        GlamourGapFinder finder)
    {
        this.log = log;
        this.gameGui = gameGui;
        this.sets = sets;
        this.scanner = scanner;
        this.finder = finder;
    }

    public void Dump()
    {
        log.Information($"--- Cratools glamour dump: {sets.SetCount} outfits, {sets.PieceCount} pieces, " +
                        $"{sets.CabinetItemCount} armoire items ---");

        DumpItemFinder();
        DumpPrismBox();
        DumpArmoire();
        DumpGaps();
        DumpInventoryAddons();
    }

    private void DumpItemFinder()
    {
        var finderModule = ItemFinderModule.Instance();
        if (finderModule == null)
        {
            log.Information("ItemFinderModule: null.");
            return;
        }

        var ids = finderModule->GlamourDresserItemIds;
        var bits = finderModule->GlamourDresserItemSetUnlockBits;

        log.Information($"ItemFinderModule: cached={finderModule->IsGlamourDresserCached}, " +
                        $"ids={ids.Length}, setBits={bits.Length}, cabinetBits={finderModule->CabinetItemUnlockBits.Length}, " +
                        $"cabinetState={finderModule->CabinetState}");

        var mirage = MirageManager.Instance();
        var live = mirage != null && mirage->PrismBoxLoaded;

        var shown = 0;
        var tokens = 0;
        var emptyMasks = 0;
        var outsideSet = 0;
        var agreesWithFilled = 0;
        var agreesWithMissing = 0;

        for (var i = 0; i < ids.Length; i++)
        {
            var raw = ids[i];
            if (raw == 0)
                continue;

            var id = GearsetIndex.Normalize(raw);
            if (!sets.IsSetToken(id))
            {
                if (shown++ < SampleRows)
                    log.Information($"  [{i,3}] raw={raw,8} id={id,6} loose");

                continue;
            }

            tokens++;

            var mask = i < bits.Length ? bits[i] : (ushort)0;
            var defined = DefinedMask(id);

            if (mask == 0)
                emptyMasks++;

            // Either reading requires the mask to stay inside the slots the set actually defines.
            if ((mask & ~defined) != 0)
                outsideSet++;

            ushort liveMask = 0;
            if (live)
            {
                for (var slot = 0; slot < GlamourSets.SetSlotCount; slot++)
                {
                    if (mirage->IsSetSlotUnlocked((uint)i, slot))
                        liveMask |= (ushort)(1 << slot);
                }

                if (liveMask == mask)
                    agreesWithFilled++;

                if (liveMask == (ushort)(defined & ~mask))
                    agreesWithMissing++;
            }

            if (shown++ < SampleRows)
            {
                var line = new StringBuilder();
                line.Append($"  [{i,3}] raw={raw,8} id={id,6} SET  {sets.SetName(id)}");
                line.Append($" bits=0x{mask:X3} defined=0x{defined:X3}");
                line.Append($" if-filled=[{Describe(id, mask)}]");
                line.Append($" if-missing=[{Describe(id, (ushort)(defined & ~mask))}]");

                if (live)
                    line.Append($" IsSetSlotUnlocked=0x{liveMask:X3} [{Describe(id, liveMask)}]");

                log.Information(line.ToString());
            }
        }

        log.Information($"Dresser entries: {tokens} outfit token(s); {emptyMasks} with an all-zero mask, " +
                        $"{outsideSet} with bits outside the set's own slots.");

        if (live)
        {
            log.Information($"Polarity cross-check against IsSetSlotUnlocked: bits-mean-filled matches " +
                            $"{agreesWithFilled}/{tokens}, bits-mean-missing matches {agreesWithMissing}/{tokens}. " +
                            $"DresserState.SetBitsMeanMissingSlots is currently " +
                            $"{DresserState.SetBitsMeanMissingSlots}.");
        }
        else
        {
            log.Information("Prism box not loaded, so the polarity could not be cross-checked. Open the " +
                            "glamour dresser and run this again — that is the one thing this dump still needs.");

            if (emptyMasks > 0)
                log.Information($"{emptyMasks} token(s) carry an all-zero mask. An outfit holding no pieces " +
                                "cannot occupy a dresser entry, so this is evidence the bits mean missing " +
                                "slots rather than filled ones.");
        }
    }

    private ushort DefinedMask(uint setId)
    {
        if (!sets.TryGetSet(setId, out var set))
            return 0;

        ushort mask = 0;
        for (var slot = 0; slot < GlamourSets.SetSlotCount; slot++)
        {
            if (set.Pieces[slot] != 0)
                mask |= (ushort)(1 << slot);
        }

        return mask;
    }

    private void DumpPrismBox()
    {
        var mirage = MirageManager.Instance();
        if (mirage == null)
        {
            log.Information("MirageManager: null.");
            return;
        }

        var ids = mirage->PrismBoxItemIds;
        var used = 0;
        var tokens = 0;
        foreach (var raw in ids)
        {
            if (raw == 0)
                continue;

            used++;
            if (sets.IsSetToken(GearsetIndex.Normalize(raw)))
                tokens++;
        }

        log.Information($"MirageManager: requested={mirage->PrismBoxRequested}, loaded={mirage->PrismBoxLoaded}, " +
                        $"entries={ids.Length}, used={used}, of which set tokens={tokens}");
    }

    private void DumpArmoire()
    {
        var uiState = UIState.Instance();
        if (uiState == null)
        {
            log.Information("UIState: null.");
            return;
        }

        var loaded = uiState->Cabinet.IsCabinetLoaded();
        var stored = 0;

        if (loaded)
        {
            foreach (var (_, cabinetId) in sets.CabinetIds)
            {
                if (uiState->Cabinet.IsItemInCabinet(cabinetId))
                    stored++;
            }
        }

        log.Information($"Armoire: loaded={loaded}, deposited={stored} of {sets.CabinetItemCount}");
    }

    private void DumpGaps()
    {
        var state = DresserState.Capture(sets);
        log.Information($"DresserState: source={state.Source}, entries={state.DresserEntryCount}, " +
                        $"loose={state.LooseItemCount}, storedSets={state.StoredSetCount}, " +
                        $"armoireKnown={state.ArmoireKnown} ({state.ArmoireItemCount} items)");

        var scanned = scanner.Scan(includeEquipped: true, includeBags: true);
        var report = finder.Find(scanned, state);

        log.Information($"Gaps: {report.ActionableCount} actionable, {report.BlockedCount} blocked, " +
                        $"over {report.Scanned} scanned items.");

        // Which containers the marks will land in. An empty bag line explains an overlay that
        // looks broken but is merely drawing nothing.
        foreach (var group in report.GapSlots.GroupBy(slot => slot.Container).OrderBy(g => g.Key.ToString()))
            log.Information($"  gap slots in {group.Key}: {group.Count()}");

        var shown = 0;
        foreach (var entry in report.Entries)
        {
            if (shown++ >= SampleRows)
                break;

            log.Information($"  {entry.Kind,-22} {entry.Name} [{entry.Item.Container}:{entry.Item.Slot}] " +
                            $"{entry.Explanation}{(entry.Blocker == null ? string.Empty : $" (blocked: {entry.Blocker})")}");
        }
    }

    /// <summary>
    /// Which inventory window is on screen and which grids it is using. The three layouts use
    /// different grid addons, so this is the first thing to check when the bags show no marks.
    /// </summary>
    private void DumpInventoryAddons()
    {
        var single = (AddonInventory*)gameGui.GetAddonByName("Inventory", 1).Address;
        var large = (AddonInventoryLarge*)gameGui.GetAddonByName("InventoryLarge", 1).Address;
        var expansion = (AddonInventoryExpansion*)gameGui.GetAddonByName("InventoryExpansion", 1).Address;

        log.Information($"Inventory windows: Inventory={Describe(single == null ? null : &single->AtkUnitBase)}" +
                        $"{(single == null ? string.Empty : $" tab={single->TabIndex}")}, " +
                        $"InventoryLarge={Describe(large == null ? null : &large->AtkUnitBase)}" +
                        $"{(large == null ? string.Empty : $" tab={large->TabIndex}")}, " +
                        $"InventoryExpansion={Describe(expansion == null ? null : &expansion->AtkUnitBase)}");

        foreach (var name in GridAddons)
        {
            var grid = (AddonInventoryGrid*)gameGui.GetAddonByName(name, 1).Address;
            if (grid == null)
                continue;

            log.Information($"  grid {name}: {Describe(&grid->AtkUnitBase)}, slots={grid->Slots.Length}");
        }
    }

    private static string Describe(FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase* addon)
        => addon == null ? "absent" : addon->IsVisible ? "visible" : "hidden";

    private string Describe(uint setId, ushort mask)
    {
        if (!sets.TryGetSet(setId, out var set))
            return "?";

        var text = new StringBuilder();
        for (var slot = 0; slot < GlamourSets.SetSlotCount; slot++)
        {
            if (set.Pieces[slot] == 0 || (mask & (1 << slot)) == 0)
                continue;

            if (text.Length > 0)
                text.Append(", ");

            text.Append(GlamourSets.SetSlotNames[slot]);
        }

        return text.ToString();
    }
}
