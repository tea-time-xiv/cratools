using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Cratools.Retainers;

/// <summary>
/// The only code in Cratools that acts on the game UI instead of reading it: firing addon
/// callbacks, closing addons and clicking through dialogue. Everything else in the plugin is a
/// read-only overlay, so keep input confined here.
///
/// These are ports of the few ECommons pieces Dagobert uses (Callback.Fire, AddonMaster.Talk.Click),
/// written directly against FFXIVClientStructs so Cratools needs no ECommons dependency.
/// </summary>
public static unsafe class GameInput
{
    // AtkEventStateFlags for the Talk click, as ECommons' AddonMaster.Talk sends it.
    private const byte TalkClickFlags = 132;

    /// <summary>The addon if it is open, visible and done setting up; otherwise null.</summary>
    public static AtkUnitBase* GetReadyAddon(IGameGui gameGui, string name)
    {
        var addon = (AtkUnitBase*)gameGui.GetAddonByName(name, 1).Address;
        return addon != null && addon->IsReady && addon->IsVisible ? addon : null;
    }

    public static AtkValue Int(int value) => new() { Type = AtkValueType.Int, Int = value };

    public static AtkValue UInt(uint value) => new() { Type = AtkValueType.UInt, UInt = value };

    public static AtkValue Undefined => new() { Type = AtkValueType.Undefined, Int = 0 };

    /// <summary>Fires the addon's callback as if the player had clicked the control it stands for.</summary>
    public static void FireCallback(AtkUnitBase* addon, params AtkValue[] values)
    {
        fixed (AtkValue* ptr = values)
            addon->FireCallback((uint)values.Length, ptr, true);
    }

    /// <summary>Advances a Talk dialogue box by one line, like clicking it.</summary>
    public static void ClickTalk(AtkUnitBase* talk)
    {
        var atkEvent = new AtkEvent
        {
            Listener = (AtkEventListener*)talk,
            Target = &AtkStage.Instance()->AtkEventTarget,
            State = new AtkEventState { StateFlags = (AtkEventStateFlags)TalkClickFlags },
        };
        var data = new AtkEventData();

        talk->ReceiveEvent(AtkEventType.MouseDown, 0, &atkEvent, &data);
        talk->ReceiveEvent(AtkEventType.MouseClick, 0, &atkEvent, &data);
        talk->ReceiveEvent(AtkEventType.MouseUp, 0, &atkEvent, &data);
    }

    public static bool IsString(AtkValue value)
        => value.Type is AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString;

    /// <summary>The plain text of a string AtkValue, or null for any other type.</summary>
    public static string? ReadString(AtkValue value)
        => IsString(value) && value.String.Value != null
               ? MemoryHelper.ReadSeStringNullTerminated((nint)value.String.Value).TextValue
               : null;
}
