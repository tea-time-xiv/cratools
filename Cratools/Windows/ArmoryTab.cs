using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Cratools.Armory;
using Dalamud.Bindings.ImGui;

namespace Cratools.Windows;

/// <summary>
/// The armory cleanup tab of the main window. Every verdict is listed with the reason behind it,
/// because a junk call you cannot check is a junk call you cannot trust — this list is how the
/// rules get verified against a real armoury before anything is tinted in-game.
/// </summary>
public sealed class ArmoryTab
{
    private static readonly Vector4 JunkColor = new(1f, 0.42f, 0.38f, 1f);
    private static readonly Vector4 ProtectedColor = new(0.55f, 0.78f, 1f, 1f);
    private static readonly Vector4 MutedColor = new(0.65f, 0.65f, 0.65f, 1f);

    private readonly Plugin plugin;

    // Table column order; the table's sort specs report the clicked column by this index.
    private enum Column
    {
        Item,
        Slot,
        ItemLevel,
        Verdict,
        Why,
    }

    private ArmoryReport? report;
    private bool junkOnly = true;
    private string nameFilter = string.Empty;
    private Column sortColumn = Column.Slot;
    private bool sortAscending = true;

    public ArmoryTab(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        if (ImGui.Button("Scan armoury"))
            Scan();

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            report = null;
            plugin.ArmoryHighlighter.Clear();
        }

        ImGui.SameLine();
        ImGui.Checkbox("Junk only", ref junkOnly);

        DrawProtectionToggles();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (report == null)
        {
            ImGui.TextWrapped("Scan to list every piece in your armoury with the reason it is kept " +
                              "or considered junk. Nothing is ever discarded or moved: this plugin " +
                              "only reads and draws.");
            return;
        }

        if (!report.PlayerStateLoaded)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JunkColor);
            ImGui.TextWrapped("Character data was not loaded during the scan, so class levels are " +
                              "unknown. Scan again once you are logged in.");
            ImGui.PopStyleColor();
            return;
        }

        ImGui.TextUnformatted($"{report.JunkCount} junk of {report.Verdicts.Count} pieces " +
                              $"({report.LockedJobCount} locked class, {report.SupersededCount} superseded, " +
                              $"{report.DuplicateCount} spare); {report.ProtectedCount} protected.");

        ImGui.Spacing();
        ImGui.SetNextItemWidth(260f);
        ImGui.InputTextWithHint("##cratools_armory_filter", "Filter by name", ref nameFilter, 128);

        ImGui.Spacing();
        DrawTable();
    }

    private void DrawProtectionToggles()
    {
        var configuration = plugin.Configuration;

        var customised = configuration.ArmoryProtectCustomised;
        if (ImGui.Checkbox("Protect melded / glamoured / dyed", ref customised))
        {
            configuration.ArmoryProtectCustomised = customised;
            configuration.Save();
            Rescan();
        }

        ImGui.SameLine();

        var unique = configuration.ArmoryProtectUnique;
        if (ImGui.Checkbox("Protect unique & rare", ref unique))
        {
            configuration.ArmoryProtectUnique = unique;
            configuration.Save();
            Rescan();
        }

        var minLevel = configuration.ArmoryIgnoreJobsBelowLevel;
        ImGui.SetNextItemWidth(200f);
        if (ImGui.SliderInt("Ignore classes below level", ref minLevel, 0, 100))
        {
            configuration.ArmoryIgnoreJobsBelowLevel = minLevel;
            configuration.Save();
            Rescan();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Accessories are wearable by nearly every job, so one class parked at " +
                             "level 1 keeps every accessory you own \"still the best\" for it and " +
                             "nothing ever counts as redundant. Raising this ignores those classes.\n\n" +
                             "It also makes gear only those classes can use count as junk.");
        }

        ImGui.TextColored(MutedColor, "Gearset items and worn gear are always protected.");
    }

    private void DrawTable()
    {
        var filter = nameFilter.Trim();
        var matches = report!.Verdicts
                             .Where(v => filter.Length == 0 ||
                                         v.Facts.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                             .ToList();
        var shown = matches.Where(v => !junkOnly || v.IsJunk).ToList();

        if (shown.Count == 0)
        {
            ImGui.TextColored(MutedColor, EmptyMessage(filter, matches.Count));
            return;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                                      ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable |
                                      ImGuiTableFlags.Sortable;

        if (!ImGui.BeginTable("##cratools_armory", 5, flags, new Vector2(-1, -1)))
            return;

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 3f);
        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort, 90f);
        ImGui.TableSetupColumn("ilvl", ImGuiTableColumnFlags.WidthFixed, 45f);
        ImGui.TableSetupColumn("Verdict", ImGuiTableColumnFlags.WidthFixed, 110f);
        ImGui.TableSetupColumn("Why", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoSort, 4f);
        ImGui.TableSetupScrollFreeze(0, 1);
        ReadSortSpecs();
        ImGui.TableHeadersRow();

        var rows = Sort(shown);

        for (var i = 0; i < rows.Count; i++)
        {
            var verdict = rows[i];
            ImGui.TableNextRow();
            ImGui.PushID(i);

            ImGui.TableNextColumn();
            var name = verdict.Item.IsHq ? verdict.Facts.Name + " (HQ)" : verdict.Facts.Name;
            ImGui.TextColored(ColorFor(verdict), name);
            DrawRowContextMenu(verdict);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(verdict.Facts.Slot.ToString());

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(verdict.Facts.ItemLevel.ToString());

            ImGui.TableNextColumn();
            ImGui.TextColored(ColorFor(verdict), VerdictLabel(verdict));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(verdict.Explanation);

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private string EmptyMessage(string filter, int matchCount)
    {
        if (filter.Length == 0)
            return junkOnly ? "No junk found." : "Nothing to show.";

        // The name did match, but only kept gear that "Junk only" hides — the usual reason a search
        // for one specific piece comes up empty.
        if (matchCount > 0)
            return $"No junk matches \"{filter}\". Untick \"Junk only\" to search kept gear too.";

        return $"Nothing matches \"{filter}\".";
    }

    private void ReadSortSpecs()
    {
        var specs = ImGui.TableGetSortSpecs();
        if (specs.IsNull || !specs.SpecsDirty)
            return;

        // No SortMulti, so there is at most one spec: the column last clicked.
        if (specs.SpecsCount > 0)
        {
            sortColumn = (Column)specs.Specs.ColumnIndex;
            sortAscending = specs.Specs.SortDirection == ImGuiSortDirection.Ascending;
        }

        specs.SpecsDirty = false;
    }

    /// <summary>
    /// The clicked column decides the order; the default slot / junk-first / item-level order breaks
    /// ties, so rows with equal keys stay put instead of shuffling.
    /// </summary>
    private List<ArmoryVerdict> Sort(List<ArmoryVerdict> rows)
    {
        var ordered = sortColumn switch
        {
            Column.Item => By(rows, v => v.Facts.Name, StringComparer.OrdinalIgnoreCase),
            Column.ItemLevel => By(rows, v => v.Facts.ItemLevel),
            Column.Verdict => By(rows, VerdictLabel, StringComparer.Ordinal),
            _ => By(rows, v => v.Facts.Slot),
        };

        return ordered.ThenBy(v => v.Facts.Slot)
                      .ThenByDescending(v => v.IsJunk)
                      .ThenBy(v => v.Facts.ItemLevel)
                      .ThenBy(v => v.Facts.Name, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(v => v.Item.IsHq)
                      .ToList();
    }

    private IOrderedEnumerable<ArmoryVerdict> By<TKey>(IEnumerable<ArmoryVerdict> rows,
                                                       Func<ArmoryVerdict, TKey> key,
                                                       IComparer<TKey>? comparer = null) =>
        sortAscending ? rows.OrderBy(key, comparer) : rows.OrderByDescending(key, comparer);

    private void DrawRowContextMenu(ArmoryVerdict verdict)
    {
        if (!ImGui.BeginPopupContextItem("##keep"))
            return;

        var keepList = plugin.Configuration.ArmoryKeepList;
        var onList = keepList.Contains(verdict.Item.ItemId);

        if (ImGui.MenuItem(onList ? "Remove from keep list" : "Always keep this item"))
        {
            if (onList)
                keepList.Remove(verdict.Item.ItemId);
            else
                keepList.Add(verdict.Item.ItemId);

            plugin.Configuration.Save();
            Rescan();
        }

        ImGui.EndPopup();
    }

    private static Vector4 ColorFor(ArmoryVerdict verdict)
    {
        if (verdict.IsJunk)
            return JunkColor;

        return verdict.Keep == KeepReason.NoBetterOwned ? MutedColor : ProtectedColor;
    }

    private static string VerdictLabel(ArmoryVerdict verdict) => verdict.Kind switch
    {
        VerdictKind.JunkLockedJob => "Junk: locked",
        VerdictKind.JunkSuperseded => "Junk: outclassed",
        VerdictKind.JunkDuplicate => "Junk: spare",
        _ => verdict.Keep switch
        {
            KeepReason.NoBetterOwned => "Keep",
            KeepReason.Gearset => "Keep: gearset",
            KeepReason.Customised => "Keep: invested",
            KeepReason.Unique => "Keep: unique",
            KeepReason.KeepList => "Keep: pinned",
            _ => "Keep",
        },
    };

    private void Rescan()
    {
        if (report != null)
            Scan();
    }

    private void Scan()
    {
        plugin.JobUnlockState.MinimumRelevantLevel = plugin.Configuration.ArmoryIgnoreJobsBelowLevel;
        plugin.JobUnlockState.Refresh();
        plugin.GearsetIndex.Refresh();

        var scanned = plugin.ArmoryScanner.Scan();
        report = plugin.ArmoryAnalyzer.Analyze(scanned);

        plugin.ArmoryHighlighter.SetJunk(report.JunkSlots);
    }
}
