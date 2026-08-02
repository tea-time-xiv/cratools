using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Cratools.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin)
        : base("Cratools Settings###CratoolsConfigWindow")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(420, 330);
        SizeCondition = ImGuiCond.Always;

        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var enabled = configuration.HighlightEnabled;
        if (ImGui.Checkbox("Enable inventory fade", ref enabled))
        {
            configuration.HighlightEnabled = enabled;
            configuration.Save();
        }

        ImGui.Spacing();

        var opacity = configuration.FadeOpacity;
        if (ImGui.SliderFloat("Fade opacity", ref opacity, 0.1f, 0.95f))
        {
            configuration.FadeOpacity = opacity;
            configuration.Save();
        }

        ImGui.TextWrapped("How strongly the slots you keep are dimmed.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextWrapped("The fade only shows in the 'all bags' (InventoryExpansion) window while " +
                          "a list is applied. Use /cratools dump to log the inventory node tree " +
                          "for troubleshooting slot alignment.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var armoryEnabled = configuration.ArmoryHighlightEnabled;
        if (ImGui.Checkbox("Enable armoury junk tint", ref armoryEnabled))
        {
            configuration.ArmoryHighlightEnabled = armoryEnabled;
            configuration.Save();
        }

        var tint = configuration.ArmoryTintOpacity;
        if (ImGui.SliderFloat("Junk tint opacity", ref tint, 0.1f, 0.8f))
        {
            configuration.ArmoryTintOpacity = tint;
            configuration.Save();
        }

        ImGui.TextWrapped("Gear the armory scan called junk is tinted red in the Armoury Chest. " +
                          "Run the scan from /cratools armory first.");
    }
}
