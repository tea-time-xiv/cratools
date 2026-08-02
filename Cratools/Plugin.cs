using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Cratools.Armory;
using Cratools.Windows;

namespace Cratools;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;

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

    public readonly WindowSystem WindowSystem = new("Cratools");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private ArmoryWindow ArmoryWindow { get; init; }

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

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        ArmoryWindow = new ArmoryWindow(this);
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(ArmoryWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Cratools window. \"armory\" opens the armory cleanup list, " +
                          "\"armorydump\" logs armoury diagnostics.",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.Draw += Highlighter.Draw;
        PluginInterface.UiBuilder.Draw += ArmoryHighlighter.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("Cratools loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.Draw -= Highlighter.Draw;
        PluginInterface.UiBuilder.Draw -= ArmoryHighlighter.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        MainWindow.Dispose();
        ArmoryWindow.Dispose();

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

        if (argument.Equals("armory", StringComparison.OrdinalIgnoreCase))
        {
            ArmoryWindow.Toggle();
            return;
        }

        ToggleMainUi();
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
