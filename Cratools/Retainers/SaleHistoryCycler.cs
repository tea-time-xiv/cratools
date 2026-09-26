using System;
using System.Collections.Generic;
using System.Diagnostics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Cratools.Retainers;

/// <summary>
/// Visits every active retainer in turn and opens its sale history, holding it open long enough
/// for Cashflow (a separate plugin) to record it, then moves on. Started from the button on the
/// retainer list, it stops once each retainer has been visited once.
///
/// Per retainer, as walked by hand under "/cratools retainerdump":
///
///  1. Click the retainer's row. RetainerList closes; a Talk greeting and the retainer menu
///     (SelectString) open.
///  2. Pick "View sale history.". SelectString closes and RetainerHistory opens; its rows arrive
///     about 350 ms later.
///  3. Hold it open for <see cref="Configuration.SaleHistoryDwellMs"/>.
///  4. Close RetainerHistory. A new SelectString opens.
///  5. Close SelectString. A Talk farewell shows; clicking through it reopens RetainerList.
///
/// Every step waits for its addon to be ready and gives up after <see cref="StepTimeoutMs"/>,
/// which stops the whole run: a step that never completes means the game is not where the
/// sequence thinks it is, and guessing onward would click things blind. The Talk lines are clicked
/// through by a lifecycle listener that only exists while a run is going.
///
/// This drives the game UI (through <see cref="GameInput"/>), unlike the rest of the plugin.
/// </summary>
public sealed unsafe class SaleHistoryCycler : IDisposable
{
    private const int StepTimeoutMs = 10_000;

    // Pause after an addon becomes ready before clicking it, as Dagobert does, so the click does not
    // land in the same frame the addon finishes setting up.
    private const int SettleMs = 100;

    private readonly IGameGui gameGui;
    private readonly IFramework framework;
    private readonly IAddonLifecycle lifecycle;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly AutoRetainerSuppressor autoRetainer;

    private readonly Queue<Step> steps = new();
    private readonly Stopwatch stepClock = new();

    private int visited;
    private int total;

    public SaleHistoryCycler(IDalamudPluginInterface pluginInterface, IGameGui gameGui, IFramework framework,
                             IAddonLifecycle lifecycle, IChatGui chat, IPluginLog log, Configuration configuration)
    {
        this.gameGui = gameGui;
        this.framework = framework;
        this.lifecycle = lifecycle;
        this.chat = chat;
        this.log = log;
        this.configuration = configuration;
        autoRetainer = new AutoRetainerSuppressor(pluginInterface, log);
    }

    public bool IsRunning { get; private set; }

    /// <summary>"2/5" while running, for the button.</summary>
    public string Progress => $"{visited}/{total}";

    public void Dispose()
    {
        if (IsRunning)
            Stop();
    }

    public void Start()
    {
        if (IsRunning)
            return;

        var list = GameInput.GetReadyAddon(gameGui, RetainerAddons.RetainerList);
        if (list == null)
            return;

        var retainers = RetainerAddons.ReadRetainers(list);
        steps.Clear();
        visited = 0;
        total = 0;

        foreach (var retainer in retainers)
        {
            if (!retainer.Active)
            {
                log.Information($"Sale history: skipping inactive retainer \"{retainer.Name}\"");
                continue;
            }

            total++;
            EnqueueRetainer(retainer);
        }

        if (total == 0)
        {
            chat.PrintError("[Cratools] No active retainers to visit.");
            return;
        }

        IsRunning = true;
        stepClock.Restart();
        autoRetainer.Suppress();
        lifecycle.RegisterListener(AddonEvent.PostUpdate, RetainerAddons.Talk, OnTalkUpdate);
        framework.Update += OnFrameworkUpdate;

        log.Information($"Sale history: visiting {total} retainer(s)");
    }

    public void Cancel()
    {
        if (!IsRunning)
            return;

        Stop();
        chat.Print($"[Cratools] Sale history run cancelled after {visited}/{total} retainer(s).");
    }

    private void EnqueueRetainer(RetainerAddons.RetainerRow retainer)
    {
        var index = total;

        Enqueue($"open {retainer.Name}", () => OpenRetainer(retainer, index));
        Enqueue("open the sale history", OpenSaleHistory);
        Enqueue("wait for the sale history", () => AddonIsReady(RetainerAddons.SaleHistory));
        Wait(configuration.SaleHistoryDwellMs);
        Enqueue("close the sale history", () => CloseAddon(RetainerAddons.SaleHistory));
        Enqueue("close the retainer", () => CloseAddon(RetainerAddons.RetainerMenu));
        Enqueue("return to the retainer list", () => AddonIsReady(RetainerAddons.RetainerList));
    }

    // --- Steps: each returns true once done, false to be tried again next frame ---

    private bool OpenRetainer(RetainerAddons.RetainerRow retainer, int index)
    {
        var list = GameInput.GetReadyAddon(gameGui, RetainerAddons.RetainerList);
        if (list == null || !HasSettled())
            return false;

        // The list is rebuilt after every visit and fills in over a frame or two; wait until the
        // row holds the retainer read at the start, so a re-sorted or half-filled list is never
        // clicked blind.
        var rows = RetainerAddons.ReadRetainers(list);
        if (retainer.Row >= rows.Count || rows[retainer.Row].Name != retainer.Name)
            return false;

        visited = index;
        chat.Print($"[Cratools] Sale history: {retainer.Name} ({index}/{total})");
        RetainerAddons.SelectRetainer(list, retainer.Row);
        return true;
    }

    private bool OpenSaleHistory()
    {
        var menu = GameInput.GetReadyAddon(gameGui, RetainerAddons.RetainerMenu);
        if (menu == null || !HasSettled())
            return false;

        var entry = RetainerAddons.FindSaleHistoryEntry(menu, out var matchedByText);
        if (entry < 0)
            throw new InvalidOperationException("the retainer menu has no sale history entry");

        if (!matchedByText)
            log.Warning($"Sale history: menu text did not match, using entry {entry} by position");

        RetainerAddons.SelectMenuEntry(menu, entry);
        return true;
    }

    private bool AddonIsReady(string name) => GameInput.GetReadyAddon(gameGui, name) != null;

    private bool CloseAddon(string name)
    {
        var addon = GameInput.GetReadyAddon(gameGui, name);
        if (addon == null || !HasSettled())
            return false;

        addon->Close(true);
        return true;
    }

    // --- Runner ---

    private void Enqueue(string name, Func<bool> run) => steps.Enqueue(new Step(name, run, StepTimeoutMs));

    private void Wait(int ms)
        => steps.Enqueue(new Step($"wait {ms} ms", () => stepClock.ElapsedMilliseconds >= ms, ms + StepTimeoutMs));

    // Steps that click need their addon to have been ready for a moment; the step clock restarts
    // whenever a step completes, so this is "SettleMs since the previous step finished" at least.
    private bool HasSettled() => stepClock.ElapsedMilliseconds >= SettleMs;

    private void OnFrameworkUpdate(IFramework _)
    {
        if (steps.Count == 0)
        {
            visited = total;
            Stop();
            chat.Print($"[Cratools] Sale history viewed for {total} retainer(s).");
            return;
        }

        var step = steps.Peek();
        bool done;
        try
        {
            done = step.Run();
        }
        catch (Exception ex)
        {
            log.Error(ex, $"Sale history: step \"{step.Name}\" failed");
            Abort($"{step.Name} failed: {ex.Message}");
            return;
        }

        if (done)
        {
            steps.Dequeue();
            stepClock.Restart();
            return;
        }

        if (stepClock.ElapsedMilliseconds > step.TimeoutMs)
            Abort($"timed out trying to {step.Name}");
    }

    private void OnTalkUpdate(AddonEvent type, AddonArgs args)
    {
        if (!IsRunning)
            return;

        var talk = (AtkUnitBase*)args.Addon.Address;
        if (talk != null && talk->IsVisible)
            GameInput.ClickTalk(talk);
    }

    private void Abort(string reason)
    {
        Stop();
        log.Warning($"Sale history: stopped at {visited}/{total}: {reason}");
        chat.PrintError($"[Cratools] Sale history stopped at {visited}/{total}: {reason}.");
    }

    // Leaves whatever window is open as it is: closing things blind after a failure is how an
    // automation ends up clicking something it should not.
    private void Stop()
    {
        IsRunning = false;
        framework.Update -= OnFrameworkUpdate;
        lifecycle.UnregisterListener(AddonEvent.PostUpdate, RetainerAddons.Talk, OnTalkUpdate);
        autoRetainer.Restore();
        steps.Clear();
        stepClock.Reset();
    }

    private sealed record Step(string Name, Func<bool> Run, int TimeoutMs);
}
