using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Cratools;

/// <summary>
/// Draws over an inventory slot node. Shared by the inventory fade, the armoury junk tint and the
/// glamour-gap marker: all of them read the node's screen rect and paint on the ImGui background
/// draw list, never touching game memory.
///
/// Fills and borders are kept apart on purpose. A slot can be junk *and* worth storing at the same
/// time, and two translucent fills stacked on one slot blend into a third colour that means
/// nothing; a fill under a border stays readable as both.
/// </summary>
public static unsafe class SlotOverlay
{
    public static void DrawRect(AtkResNode* node, float scale, ImDrawListPtr drawList, uint color)
    {
        if (node == null)
            return;

        var pos = new Vector2(node->ScreenX, node->ScreenY);
        var size = new Vector2(node->Width * scale, node->Height * scale);
        drawList.AddRectFilled(pos, pos + size, color);
    }

    /// <summary>Outlines the slot. Drawn inset by half the stroke so it stays inside the slot.</summary>
    public static void DrawBorder(AtkResNode* node, float scale, ImDrawListPtr drawList, uint color,
                                  float thickness = 2f)
    {
        if (node == null)
            return;

        var stroke = thickness * scale;
        var inset = new Vector2(stroke * 0.5f, stroke * 0.5f);
        var pos = new Vector2(node->ScreenX, node->ScreenY) + inset;
        var size = new Vector2(node->Width * scale, node->Height * scale) - (inset * 2f);
        drawList.AddRect(pos, pos + size, color, 0f, ImDrawFlags.None, stroke);
    }
}
