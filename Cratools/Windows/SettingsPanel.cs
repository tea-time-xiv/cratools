using Dalamud.Bindings.ImGui;

namespace Cratools.Windows;

/// <summary>
/// The settings controls, drawn both as a tab of the main window and as the standalone config
/// window Dalamud opens from the plugin installer.
/// </summary>
public static class SettingsPanel
{
    public static void Draw(Configuration configuration)
    {
        ImGui.TextUnformatted("Inventory cleanup");
        ImGui.Spacing();

        var enabled = configuration.HighlightEnabled;
        if (ImGui.Checkbox("Enable inventory fade", ref enabled))
        {
            configuration.HighlightEnabled = enabled;
            configuration.Save();
        }

        var opacity = configuration.FadeOpacity;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Fade opacity", ref opacity, 0.1f, 0.95f))
        {
            configuration.FadeOpacity = opacity;
            configuration.Save();
        }

        ImGui.TextWrapped("How strongly the slots you keep are dimmed. The fade only shows in the " +
                          "'all bags' (InventoryExpansion) window while a list is applied.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted("Armory cleanup");
        ImGui.Spacing();

        var armoryEnabled = configuration.ArmoryHighlightEnabled;
        if (ImGui.Checkbox("Enable armoury junk tint", ref armoryEnabled))
        {
            configuration.ArmoryHighlightEnabled = armoryEnabled;
            configuration.Save();
        }

        var tint = configuration.ArmoryTintOpacity;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Junk tint opacity", ref tint, 0.1f, 0.8f))
        {
            configuration.ArmoryTintOpacity = tint;
            configuration.Save();
        }

        ImGui.TextWrapped("Gear the armory scan called junk is tinted red in the Armoury Chest. " +
                          "Run the scan from the Armory cleanup tab first.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted($"Keep list: {configuration.ArmoryKeepList.Count} item(s) pinned.");
        ImGui.SameLine();
        if (ImGui.Button("Empty keep list") && configuration.ArmoryKeepList.Count > 0)
        {
            configuration.ArmoryKeepList.Clear();
            configuration.Save();
        }

        ImGui.TextWrapped("Right-click any row in the armory list to pin or unpin an item.");
    }
}
