using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;

namespace Cratools.Retainers;

/// <summary>
/// The "Sale history" button drawn over the retainer list, beside Dagobert's "Auto Pinch". It is
/// right-aligned to the left edge of the venture-count box that Dagobert covers, so the two never
/// overlap when both plugins are installed.
///
/// The retainer list closes for every retainer visited, so while a run is going the button (now
/// "Cancel") stays where it was last seen instead of vanishing with the list.
/// </summary>
public sealed unsafe class RetainerListButton
{
    private const float Gap = 8f;

    private const ImGuiWindowFlags WindowFlags =
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoBackground |
        ImGuiWindowFlags.NoMove;

    private readonly IGameGui gameGui;
    private readonly SaleHistoryCycler cycler;
    private readonly Configuration configuration;

    // Top-right corner for the button, in game-window pixels, and its height.
    private Vector2? anchor;
    private float height;

    public RetainerListButton(IGameGui gameGui, SaleHistoryCycler cycler, Configuration configuration)
    {
        this.gameGui = gameGui;
        this.cycler = cycler;
        this.configuration = configuration;
    }

    /// <summary>Called every frame from UiBuilder.Draw.</summary>
    public void Draw()
    {
        if (!configuration.SaleHistoryButtonEnabled && !cycler.IsRunning)
            return;

        var list = GameInput.GetReadyAddon(gameGui, RetainerAddons.RetainerList);
        if (list != null)
        {
            var node = list->GetNodeById(RetainerAddons.ButtonAnchorNodeId);
            if (node != null)
            {
                anchor = new Vector2(node->ScreenX - Gap * list->Scale, node->ScreenY);
                height = node->Height * list->Scale;
            }
        }
        else if (!cycler.IsRunning)
        {
            return;
        }

        if (anchor is not { } position)
            return;

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().Pos + position, ImGuiCond.Always, new Vector2(1f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);

        if (ImGui.Begin("###CratoolsSaleHistory", WindowFlags))
            DrawButton();

        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    private void DrawButton()
    {
        var size = new Vector2(0f, height);

        if (cycler.IsRunning)
        {
            if (ImGui.Button($"Cancel ({cycler.Progress})###CratoolsSaleHistoryButton", size))
                cycler.Cancel();

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Stops the sale history run. Whatever window is open stays open.");

            return;
        }

        if (ImGui.Button("Sale history###CratoolsSaleHistoryButton", size))
            cycler.Start();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Opens the sale history of every active retainer in turn, so Cashflow can " +
                             "record it.\nPlease do not interact with the game while it runs.");
    }
}
