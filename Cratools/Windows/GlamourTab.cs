using System.Linq;
using System.Numerics;
using Cratools.Armory;
using Dalamud.Bindings.ImGui;

namespace Cratools.Windows;

/// <summary>
/// The glamour-collection tab: gear you are holding that the dresser (or the armoire) has never
/// seen, so it gets stored rather than discarded.
///
/// The list is grouped by outfit rather than flat: a full armoury turns up a couple of hundred
/// loose pieces, and "this outfit needs three more" is the unit the dresser actually stores in.
///
/// Scanning also feeds both overlays, so the same verdicts show up on the Armoury Chest and in the
/// 'all bags' window.
/// </summary>
public sealed class GlamourTab
{
    private static readonly Vector4 GapColor = new(1f, 0.82f, 0.35f, 1f);
    private static readonly Vector4 BlockedColor = new(0.75f, 0.62f, 0.45f, 1f);
    private static readonly Vector4 WarnColor = new(1f, 0.42f, 0.38f, 1f);
    private static readonly Vector4 MutedColor = new(0.65f, 0.65f, 0.65f, 1f);

    private readonly Plugin plugin;

    private GlamourGapReport? report;
    private bool hideBlocked;

    public GlamourTab(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        if (ImGui.Button("Scan for uncollected gear"))
            Scan();

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            report = null;
            plugin.ArmoryHighlighter.ClearGaps();
            plugin.Highlighter.ClearGaps();
        }

        ImGui.SameLine();
        ImGui.Checkbox("Hide blocked", ref hideBlocked);

        DrawToggles();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (report == null)
        {
            ImGui.TextWrapped("Finds gear in your armoury, bags and on your back that can still be " +
                              "stored as a glamour outfit set but is not in the dresser yet — the " +
                              "pieces worth keeping instead of discarding.");
            ImGui.Spacing();
            ImGui.TextColored(MutedColor, "Reading only. Nothing is stored, moved or discarded.");
            return;
        }

        DrawSourceBanner();

        ImGui.TextUnformatted($"{report.ActionableCount} to store, {report.BlockedCount} blocked, " +
                              $"out of {report.Scanned} pieces looked at.");

        ImGui.Spacing();
        DrawGroups();
    }

    private void DrawToggles()
    {
        var configuration = plugin.Configuration;

        var bags = configuration.GlamourIncludeBags;
        if (ImGui.Checkbox("Include inventory bags", ref bags))
        {
            configuration.GlamourIncludeBags = bags;
            configuration.Save();
            Rescan();
        }

        ImGui.SameLine();

        var armoire = configuration.GlamourIncludeArmoire;
        if (ImGui.Checkbox("Include armoire deposits", ref armoire))
        {
            configuration.GlamourIncludeArmoire = armoire;
            configuration.Save();
            Rescan();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Armoire-eligible gear cannot go in the glamour dresser at all, so it " +
                             "is listed as an armoire deposit instead. The armoire is free and " +
                             "unlimited, so this is usually the cheaper half of the job.");
        }
    }

    private void DrawSourceBanner()
    {
        var state = report!.State;

        switch (state.Source)
        {
            case DresserSource.None:
                ImGui.PushStyleColor(ImGuiCol.Text, WarnColor);
                ImGui.TextWrapped("The dresser contents are not known yet, so nothing can be called " +
                                  "uncollected. Open the glamour dresser once (an inn room or your " +
                                  "house will do) and scan again.");
                ImGui.PopStyleColor();
                break;

            case DresserSource.PrismBox:
                ImGui.TextColored(MutedColor,
                                  $"Read live from the open dresser: {state.LooseItemCount} loose " +
                                  $"items and {state.StoredSetCount} stored outfits.");
                break;

            case DresserSource.ItemFinderCache:
                ImGui.TextColored(MutedColor,
                                  $"Read from the game's own dresser cache: {state.LooseItemCount} loose " +
                                  $"items and {state.StoredSetCount} stored outfits. Scanning with the " +
                                  $"dresser open reads it exactly instead.");
                break;
        }

        if (!state.ArmoireKnown)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, WarnColor);
            ImGui.TextWrapped("The armoire has not been read this session, so armoire-eligible gear " +
                              "is left out. Open the armoire once and scan again.");
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
    }

    /// <summary>
    /// One collapsible section per outfit, because a full armoury produces a couple of hundred
    /// pieces and a flat list of them says nothing about the job. Grouped, the same data reads as
    /// "this outfit needs three more pieces", which is the unit the dresser actually stores in.
    /// </summary>
    private void DrawGroups()
    {
        var groups = report!.Entries
                            .Where(e => !hideBlocked || e.IsActionable)
                            .GroupBy(e => e.Kind == GlamourGapKind.StoreInArmoire ? 0u : e.SetId)
                            .Select(g => new
                            {
                                SetId = g.Key,
                                Entries = g.OrderBy(e => e.Slot).ToList(),
                                Actionable = g.Count(e => e.IsActionable),
                            })
                            .OrderBy(g => g.SetId == 0u)
                            .ThenByDescending(g => g.Actionable)
                            .ThenBy(g => g.Entries[0].SetName)
                            .ToList();

        if (groups.Count == 0)
        {
            ImGui.TextColored(MutedColor, "Nothing to store — everything you are holding is already collected.");
            return;
        }

        var outfits = groups.Count(g => g.SetId != 0u);
        ImGui.TextColored(MutedColor, outfits == 1
                                          ? "1 outfit, plus anything the armoire wants."
                                          : $"{outfits} outfits, plus anything the armoire wants.");
        ImGui.Spacing();

        if (!ImGui.BeginChild("##cratools_glamour_groups", new Vector2(-1, -1)))
        {
            ImGui.EndChild();
            return;
        }

        foreach (var group in groups)
        {
            var header = group.SetId == 0u
                             ? $"Armoire — {group.Entries.Count} to deposit"
                             : $"{group.Entries[0].SetName} — {group.Entries.Count} here, {StoredText(group.SetId)}";

            ImGui.PushStyleColor(ImGuiCol.Text, group.Actionable > 0 ? GapColor : BlockedColor);
            var open = ImGui.CollapsingHeader($"{header}###set{group.SetId}");
            ImGui.PopStyleColor();

            if (!open)
                continue;

            ImGui.Indent();
            foreach (var entry in group.Entries)
            {
                var colour = entry.IsActionable ? GapColor : BlockedColor;
                ImGui.TextColored(colour, entry.Item.IsHq ? entry.Name + " (HQ)" : entry.Name);

                ImGui.SameLine();
                ImGui.TextColored(MutedColor, entry.Blocker == null
                                                  ? $"({entry.Slot})"
                                                  : $"({entry.Slot}) — blocked: {entry.Blocker}");
            }

            ImGui.Unindent();
            ImGui.Spacing();
        }

        ImGui.EndChild();
    }

    /// <summary>"2 of 5 stored" for an outfit the dresser already holds part of.</summary>
    private string StoredText(uint setId)
    {
        if (!plugin.GlamourSets.TryGetSet(setId, out var set))
            return "not stored yet";

        var bits = report!.State.SetSlotBits(setId);
        if (bits == 0)
            return $"none of {set.PieceCount} stored";

        var stored = System.Numerics.BitOperations.PopCount(bits);
        return $"{stored} of {set.PieceCount} stored";
    }

    private void Rescan()
    {
        if (report != null)
            Scan();
    }

    private void Scan()
    {
        plugin.GearsetIndex.Refresh();

        var state = DresserState.Capture(plugin.GlamourSets);
        var scanned = plugin.ArmoryScanner.Scan(includeEquipped: true,
                                                includeBags: plugin.Configuration.GlamourIncludeBags);

        report = plugin.GlamourGapFinder.Find(scanned, state);

        plugin.ArmoryHighlighter.SetGaps(report.GapSlots);
        plugin.Highlighter.SetGaps(report.GapSlots);
    }
}
