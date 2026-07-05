using Dalamud.Configuration;
using System;

namespace Cratools;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // Master toggle for the inventory fade overlay.
    public bool HighlightEnabled { get; set; } = true;

    // Opacity of the dark rectangle drawn over "keeper" slots (0 = invisible, 1 = black).
    public float FadeOpacity { get; set; } = 0.6f;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
