using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Cratools.Retainers;

/// <summary>
/// Pauses AutoRetainer for the length of a sale-history run, so it does not start processing
/// retainers under the cycler's clicks. Dagobert does the same through ECommons' EzIPC; these are
/// the same two IPC gates ("AutoRetainer.GetSuppressed" / "AutoRetainer.SetSuppressed") over plain
/// Dalamud IPC. Without AutoRetainer loaded every call quietly does nothing.
/// </summary>
public sealed class AutoRetainerSuppressor
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;

    // What AutoRetainer's flag was before Suppress, so Restore leaves it as the player had it.
    private bool? previous;

    public AutoRetainerSuppressor(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
    }

    public void Suppress()
    {
        try
        {
            previous = pluginInterface.GetIpcSubscriber<bool>("AutoRetainer.GetSuppressed").InvokeFunc();
            pluginInterface.GetIpcSubscriber<bool, object>("AutoRetainer.SetSuppressed").InvokeAction(true);
            log.Debug($"AutoRetainer suppressed (was {previous})");
        }
        catch (Exception)
        {
            // Not installed or not loaded.
            previous = null;
        }
    }

    public void Restore()
    {
        if (previous is not { } value)
            return;

        previous = null;
        try
        {
            pluginInterface.GetIpcSubscriber<bool, object>("AutoRetainer.SetSuppressed").InvokeAction(value);
            log.Debug($"AutoRetainer suppression restored to {value}");
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Could not restore AutoRetainer's suppression flag");
        }
    }
}
