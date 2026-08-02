using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace Cratools;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // Master toggle for the inventory fade overlay.
    public bool HighlightEnabled { get; set; } = true;

    // Opacity of the dark rectangle drawn over "keeper" slots (0 = invisible, 1 = black).
    public float FadeOpacity { get; set; } = 0.6f;

    // --- Armory cleanup ---

    // Master toggle for the armoury junk tint.
    public bool ArmoryHighlightEnabled { get; set; } = true;

    // Opacity of the red rectangle drawn over junk gear.
    public float ArmoryTintOpacity { get; set; } = 0.35f;

    // Melded, glamoured or dyed gear is gear you invested in, so it is never called junk.
    public bool ArmoryProtectCustomised { get; set; } = true;

    // Unique and rare (relic-grade) gear is never called junk.
    public bool ArmoryProtectUnique { get; set; } = true;

    // Classes below this level do not count as "played", so their gear can be called redundant and
    // they no longer keep shared gear (accessories above all) alive. 0 or 1 = count every unlocked
    // class.
    public int ArmoryIgnoreJobsBelowLevel { get; set; } = 0;

    // Item ids the player marked as keep-forever.
    public HashSet<uint> ArmoryKeepList { get; set; } = new();

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
