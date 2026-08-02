using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Cratools;

/// <summary>
/// Draws a translucent rectangle over an inventory slot node. Shared by the inventory fade and the
/// armoury junk tint: both read the node's screen rect and paint on the ImGui background draw list,
/// never touching game memory.
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
}
