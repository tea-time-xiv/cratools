using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Cratools.Armory;
using Cratools.Retainers;
using Cratools.Windows;

namespace Cratools;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    private const string CommandName = "/cratools";

    public Configuration Configuration { get; init; }
    public ItemResolver Resolver { get; init; }
    public InventoryHighlighter Highlighter { get; init; }

    public EquipRules EquipRules { get; init; }
    public ArmoryScanner ArmoryScanner { get; init; }
    public GearsetIndex GearsetIndex { get; init; }
    public JobUnlockState JobUnlockState { get; init; }
    public ArmoryAnalyzer ArmoryAnalyzer { get; init; }
    public ArmoryHighlighter ArmoryHighlighter { get; init; }
    private ArmoryDebug ArmoryDebug { get; init; }

    public GlamourSets GlamourSets { get; init; }
    public GlamourGapFinder GlamourGapFinder { get; init; }
    private GlamourDebug GlamourDebug { get; init; }

    public SaleHistoryCycler SaleHistoryCycler { get; init; }
    private RetainerListButton RetainerListButton { get; init; }
    private RetainerDebug RetainerDebug { get; init; }

    public readonly WindowSystem WindowSystem = new("Cratools");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        Resolver = new ItemResolver(DataManager);
        Highlighter = new InventoryHighlighter(GameGui, Configuration);

        EquipRules = new EquipRules(DataManager, Log);
        ArmoryScanner = new ArmoryScanner();
        GearsetIndex = new GearsetIndex();
        JobUnlockState = new JobUnlockState(EquipRules);
        ArmoryAnalyzer = new ArmoryAnalyzer(EquipRules, JobUnlockState, GearsetIndex, Configuration);
        ArmoryHighlighter = new ArmoryHighlighter(GameGui, Configuration);
        ArmoryDebug = new ArmoryDebug(GameGui, Log, EquipRules, ArmoryScanner, GearsetIndex, JobUnlockState);

        GlamourSets = new GlamourSets(DataManager, Log);
        GlamourGapFinder = new GlamourGapFinder(GlamourSets, EquipRules, GearsetIndex, Configuration);
        GlamourDebug = new GlamourDebug(Log, GameGui, GlamourSets, ArmoryScanner, GlamourGapFinder);

        SaleHistoryCycler = new SaleHistoryCycler(PluginInterface, GameGui, Framework, AddonLifecycle, ChatGui, Log,
                                                  Configuration);
        RetainerListButton = new RetainerListButton(GameGui, SaleHistoryCycler, Configuration);
        RetainerDebug = new RetainerDebug(Log, GameGui, AddonLifecycle, Framework, ChatGui);

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Cratools window. \"armory\" opens the armory cleanup list, " +
                          "\"glamour\" opens the uncollected-gear list, \"armorydump\" and " +
                          "\"glamourdump\" log diagnostics, \"retainerdump\" toggles the retainer " +
                          "window watch.",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.Draw += Highlighter.Draw;
        PluginInterface.UiBuilder.Draw += ArmoryHighlighter.Draw;
        PluginInterface.UiBuilder.Draw += RetainerListButton.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("Cratools loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.Draw -= Highlighter.Draw;
        PluginInterface.UiBuilder.Draw -= ArmoryHighlighter.Draw;
        PluginInterface.UiBuilder.Draw -= RetainerListButton.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        MainWindow.Dispose();
        SaleHistoryCycler.Dispose();
        RetainerDebug.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        var argument = args.Trim();

        if (argument.Equals("armorydump", StringComparison.OrdinalIgnoreCase))
        {
            ArmoryDebug.Dump();
            return;
        }

        if (argument.Equals("glamourdump", StringComparison.OrdinalIgnoreCase))
        {
            GlamourDebug.Dump();
            return;
        }

        if (argument.Equals("retainerdump", StringComparison.OrdinalIgnoreCase))
        {
            RetainerDebug.Toggle();
            return;
        }

        if (argument.Equals("armory", StringComparison.OrdinalIgnoreCase))
        {
            MainWindow.ShowArmory();
            return;
        }

        if (argument.Equals("glamour", StringComparison.OrdinalIgnoreCase))
        {
            MainWindow.ShowGlamour();
            return;
        }

        ToggleMainUi();
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
