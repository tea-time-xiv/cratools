using System;
using System.Collections.Generic;
using System.Diagnostics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Cratools.Retainers;

/// <summary>
/// Diagnostics for the retainer sale-history feature, run with "/cratools retainerdump".
///
/// Unlike the armory and glamour dumps this one is a toggle. The first run logs a snapshot and
/// starts watching: every non-HUD addon that opens, refreshes, updates or closes is logged with a
/// timestamp, its AtkValues, and any list component whose row count changes afterwards. Walk one
/// retainer by hand (bell â†’ retainer â†’ "View sale history" â†’ close â†’ close), then run it again to
/// stop. It settles what only the running game can confirm. As of the 2026-09-26 run:
///
///  1. Settled: the sale-history addon is "RetainerHistory". FFXIVClientStructs has no struct for it.
///  2. Settled: the retainer menu (SelectString) holds its entry count in v[3] and the entries from
///     v[7]; "View sale history." is entry 4 of 13.
///  3. Settled: RetainerList is blocks of ten from v[3] (name +0, active flag +8), v[2] the count,
///     in the same order as RetainerManager.DisplayOrder.
///  4. Settled: the button anchors to node id 2, the venture-count box Dagobert draws over.
///  5. Settled: RetainerHistory opens empty and its rows arrive ~350 ms later (a PostRefresh with
///     the row count in v[0]), well inside the two-second dwell.
///  6. Settled: picking a retainer closes RetainerList; it reopens only after the Talk farewell
///     that follows closing the retainer menu. Talk is never set up anew, only refreshed.
///
/// All of it lives in <see cref="RetainerAddons"/>; rerun this after a patch that moves the
/// retainer windows.
///
/// Read-only: it only reads addons and logs.
/// </summary>
public sealed unsafe class RetainerDebug : IDisposable
{
    private const int MaxValues = 160;
    private const int RetainerBlockStart = 3;
    private const int RetainerBlockStride = 10;

    private static readonly AddonEvent[] WatchedEvents =
    {
        AddonEvent.PostSetup, AddonEvent.PostRefresh, AddonEvent.PostRequestedUpdate, AddonEvent.PreFinalize,
    };

    // Non-HUD addons that update constantly and say nothing about retainers.
    private static readonly HashSet<string> NoisyAddons = new(StringComparer.Ordinal)
    {
        "ChatLog", "ChatLogPanel_0", "ChatLogPanel_1", "ChatLogPanel_2", "ChatLogPanel_3", "NamePlate",
        "Tooltip", "ItemDetail", "ActionDetail", "ScreenFrameSystem", "ContextMenu", "CastBarEnemy",
        "OperationGuide",
    };

    private readonly IPluginLog log;
    private readonly IGameGui gameGui;
    private readonly IAddonLifecycle lifecycle;
    private readonly IFramework framework;
    private readonly IChatGui chat;

    private readonly Stopwatch clock = new();

    // Per open addon, the list components found at setup and the row count last logged for each.
    private readonly Dictionary<string, List<TrackedList>> trackedLists = new(StringComparer.Ordinal);

    private bool watching;

    public RetainerDebug(IPluginLog log, IGameGui gameGui, IAddonLifecycle lifecycle, IFramework framework,
                         IChatGui chat)
    {
        this.log = log;
        this.gameGui = gameGui;
        this.lifecycle = lifecycle;
        this.framework = framework;
        this.chat = chat;
    }

    public void Toggle()
    {
        Snapshot();

        if (watching)
            StopWatching();
        else
            StartWatching();
    }

    public void Dispose()
    {
        if (watching)
            StopWatching();
    }

    private void StartWatching()
    {
        watching = true;
        clock.Restart();
        trackedLists.Clear();

        foreach (var type in WatchedEvents)
            lifecycle.RegisterListener(type, OnAddonEvent);

        framework.Update += OnFrameworkUpdate;

        log.Information("--- Cratools retainer watch: ON. Walk one retainer by hand, then run " +
                        "/cratools retainerdump again. ---");
        chat.Print("[Cratools] Retainer watch on. Visit one retainer's sale history by hand, then run " +
                   "/cratools retainerdump again. Output goes to /xllog.");
    }

    private void StopWatching()
    {
        watching = false;
        framework.Update -= OnFrameworkUpdate;

        foreach (var type in WatchedEvents)
            lifecycle.UnregisterListener(type, OnAddonEvent);

        trackedLists.Clear();
        clock.Stop();

        log.Information("--- Cratools retainer watch: OFF ---");
        chat.Print("[Cratools] Retainer watch off.");
    }

    // --- Snapshot ---

    private void Snapshot()
    {
        log.Information("--- Cratools retainer dump ---");

        DumpRetainerManager();
        DumpRetainerList();

        var menu = GetAddon("SelectString");
        if (menu != null)
        {
            log.Information($"=== SelectString: ready={menu->IsReady} visible={menu->IsVisible} ===");
            DumpValues(menu->AtkValues, menu->AtkValuesCount);
        }
    }

    private void DumpRetainerManager()
    {
        var manager = RetainerManager.Instance();
        if (manager == null)
        {
            log.Information("RetainerManager: null.");
            return;
        }

        log.Information($"=== RetainerManager: count={manager->GetRetainerCount()} ===");

        // DisplayOrder maps the list's row to the Retainers slot; the RetainerList rows below
        // should appear in this order.
        var order = manager->DisplayOrder;
        for (var row = 0; row < order.Length; row++)
        {
            var slot = order[row];
            if (slot >= manager->Retainers.Length)
                continue;

            var retainer = manager->Retainers[slot];
            if (retainer.RetainerId == 0)
                continue;

            log.Information($"  row {row} â†’ slot {slot}: \"{retainer.NameString}\" available={retainer.Available} " +
                            $"level={retainer.Level} market={retainer.MarketItemCount} " +
                            $"id={retainer.RetainerId:X}");
        }
    }

    private void DumpRetainerList()
    {
        var addon = GetAddon("RetainerList");
        if (addon == null)
        {
            log.Information("=== RetainerList: not open ===");
            return;
        }

        log.Information($"=== RetainerList: ready={addon->IsReady} visible={addon->IsVisible} " +
                        $"values={addon->AtkValuesCount} scale={addon->Scale} " +
                        $"pos=({addon->X}, {addon->Y}) ===");

        DumpValues(addon->AtkValues, addon->AtkValuesCount);

        // Hypothesis under test (ECommons' reader): ten values per retainer from index 3, name at
        // +0, level +2, inventory +3, gil +4, active +8. If the decoded names match the window and
        // the active flags match "available" above, the cycler can read the list exactly so.
        log.Information("  decoded as blocks of ten from index 3:");
        for (var row = 0; ; row++)
        {
            var start = RetainerBlockStart + row * RetainerBlockStride;
            if (start + RetainerBlockStride > addon->AtkValuesCount)
                break;

            var nameValue = addon->AtkValues[start];
            if (nameValue.Type is not (AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString))
                break;

            log.Information($"    row {row}: name={Format(nameValue)} level={Format(addon->AtkValues[start + 2])} " +
                            $"inventory={Format(addon->AtkValues[start + 3])} gil={Format(addon->AtkValues[start + 4])} " +
                            $"active={Format(addon->AtkValues[start + 8])}");
        }

        log.Information($"  nodes ({addon->UldManager.NodeListCount}), for the button anchor:");
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null)
                continue;

            log.Information($"    [{i,2}] id={node->NodeId,3} type={(int)node->Type,4} " +
                            $"local=({node->X:0},{node->Y:0}) screen=({node->ScreenX:0},{node->ScreenY:0}) " +
                            $"size={node->Width}x{node->Height} vis={node->IsVisible()}");
        }
    }

    // --- Watch ---

    private void OnAddonEvent(AddonEvent type, AddonArgs args)
    {
        var name = args.AddonName;
        if (IsNoise(name))
            return;

        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null)
            return;

        var stamp = $"[+{clock.ElapsedMilliseconds,6} ms] {name} {type}";

        switch (type)
        {
            case AddonEvent.PostSetup:
                log.Information($"{stamp}: ready={addon->IsReady} visible={addon->IsVisible} " +
                                $"values={addon->AtkValuesCount}");
                DumpValues(addon->AtkValues, addon->AtkValuesCount);
                TrackLists(name, addon);
                break;

            case AddonEvent.PostRefresh when args is AddonRefreshArgs refresh:
                log.Information($"{stamp}: values={refresh.AtkValueCount}");
                DumpValues((AtkValue*)refresh.AtkValues, (int)refresh.AtkValueCount);
                break;

            case AddonEvent.PreFinalize:
                log.Information(stamp);
                trackedLists.Remove(name);
                break;

            default:
                log.Information(stamp);
                break;
        }
    }

    private void TrackLists(string name, AtkUnitBase* addon)
    {
        var lists = new List<TrackedList>();

        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var list = AsList(addon->UldManager.NodeList[i]);
            if (list == null)
                continue;

            var length = list->ListLength;
            lists.Add(new TrackedList { NodeIndex = i, Length = length });
            log.Information($"    list at node [{i}]: {length} rows");
        }

        if (lists.Count > 0)
            trackedLists[name] = lists;
    }

    // Rows usually arrive from the server after the addon is set up; log each change in a list's
    // row count so the gap between setup and "loaded" can be read off the timestamps.
    private void OnFrameworkUpdate(IFramework _)
    {
        foreach (var (name, lists) in trackedLists)
        {
            var addon = GetAddon(name);
            if (addon == null)
                continue;

            foreach (var tracked in lists)
            {
                if (tracked.NodeIndex >= addon->UldManager.NodeListCount)
                    continue;

                var list = AsList(addon->UldManager.NodeList[tracked.NodeIndex]);
                if (list == null || list->ListLength == tracked.Length)
                    continue;

                log.Information($"[+{clock.ElapsedMilliseconds,6} ms] {name} list at node [{tracked.NodeIndex}]: " +
                                $"{tracked.Length} â†’ {list->ListLength} rows (ready={addon->IsReady})");
                tracked.Length = list->ListLength;
            }
        }
    }

    // --- Helpers ---

    private static bool IsNoise(string name) => name.StartsWith('_') || NoisyAddons.Contains(name);

    private AtkUnitBase* GetAddon(string name) => (AtkUnitBase*)gameGui.GetAddonByName(name, 1).Address;

    private static AtkComponentList* AsList(AtkResNode* node)
    {
        if (node == null || (ushort)node->Type < 1000)
            return null;

        var component = ((AtkComponentNode*)node)->Component;
        if (component == null)
            return null;

        var kind = component->GetComponentType();
        return kind is ComponentType.List or ComponentType.TreeList ? (AtkComponentList*)component : null;
    }

    private void DumpValues(AtkValue* values, int count)
    {
        if (values == null || count == 0)
            return;

        var shown = Math.Min(count, MaxValues);
        for (var i = 0; i < shown; i++)
        {
            if (values[i].Type == AtkValueType.Undefined)
                continue;

            log.Information($"    v[{i,3}] {values[i].Type,-14} {Format(values[i])}");
        }

        if (count > shown)
            log.Information($"    â€¦ {count - shown} more values not shown");
    }

    private static string Format(AtkValue value) => value.Type switch
    {
        AtkValueType.Int => value.Int.ToString(),
        AtkValueType.UInt => value.UInt.ToString(),
        AtkValueType.Bool => value.Byte != 0 ? "true" : "false",
        AtkValueType.Float => value.Float.ToString("0.###"),
        AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString =>
            value.String.Value == null
                ? "(null)"
                : $"\"{MemoryHelper.ReadSeStringNullTerminated((nint)value.String.Value).TextValue}\"",
        _ => "",
    };

    private sealed class TrackedList
    {
        public int NodeIndex { get; init; }
        public int Length { get; set; }
    }
}
