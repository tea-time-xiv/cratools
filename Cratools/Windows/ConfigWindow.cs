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
        Size = new Vector2(420, 200);
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
    }
}
