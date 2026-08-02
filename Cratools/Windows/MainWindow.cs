using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Cratools.Windows;

/// <summary>
/// The plugin's single window. Both cleanup features live here as tabs so everything is reachable
/// from /cratools and from the plugin installer's main-UI button, rather than through commands.
/// </summary>
public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly ArmoryTab armoryTab;

    private string pasteText = string.Empty;
    private int matchedItems;
    private readonly List<string> unmatched = new();

    // Set when something asks for the armory tab specifically; consumed by the next Draw.
    private bool selectArmory;

    public MainWindow(Plugin plugin)
        : base("Cratools##CratoolsMainWindow")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(620, 400),
            MaximumSize = new Vector2(1400, 1200),
        };

        this.plugin = plugin;
        armoryTab = new ArmoryTab(plugin);
    }

    public void Dispose() { }

    /// <summary>Opens the window on the armory tab.</summary>
    public void ShowArmory()
    {
        IsOpen = true;
        selectArmory = true;
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##cratools_tabs"))
            return;

        if (ImGui.BeginTabItem("Inventory cleanup"))
        {
            DrawInventoryTab();
            ImGui.EndTabItem();
        }

        var armoryFlags = selectArmory ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        selectArmory = false;

        if (ImGui.BeginTabItem("Armory cleanup", armoryFlags))
        {
            armoryTab.Draw();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Settings"))
        {
            SettingsPanel.Draw(plugin.Configuration);
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawInventoryTab()
    {
        ImGui.TextWrapped("Paste Teamcraft's inventory-cleanup list below, then Apply. " +
                          "Slots you need to keep get dimmed in the 'all bags' window; " +
                          "the removable items stay bright.");
        ImGui.Spacing();

        ImGui.InputTextMultiline("##cratools_paste", ref pasteText, 32768, new Vector2(-1, 220));

        ImGui.Spacing();

        if (ImGui.Button("Apply"))
            Apply();

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            pasteText = string.Empty;
            unmatched.Clear();
            matchedItems = 0;
            plugin.Highlighter.Clear();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (plugin.Highlighter.RemovableCount > 0)
        {
            ImGui.TextUnformatted($"Active: {matchedItems} item(s) marked removable " +
                                  $"({plugin.Highlighter.RemovableCount} ids).");
        }
        else
        {
            ImGui.TextUnformatted("No list applied.");
        }

        if (unmatched.Count > 0)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.6f, 0.2f, 1f));
            ImGui.TextUnformatted($"{unmatched.Count} name(s) not found:");
            ImGui.PopStyleColor();
            foreach (var name in unmatched)
                ImGui.BulletText(name);
        }
    }

    private void Apply()
    {
        unmatched.Clear();
        var entries = CleanupList.Parse(pasteText);

        var ids = new HashSet<uint>();
        matchedItems = 0;
        foreach (var entry in entries)
        {
            if (plugin.Resolver.TryResolve(entry.Name, out var id))
            {
                if (ids.Add(id))
                    matchedItems++;
            }
            else
            {
                unmatched.Add(entry.Name);
            }
        }

        plugin.Highlighter.SetRemovable(ids);
    }
}
