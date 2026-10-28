using Dalamud.Configuration;

namespace GearScout;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;
    public List<TrackedSet> Sets { get; set; } = [];
    public Dictionary<string, Guid> SelectedByJob { get; set; } = [];
    public bool HighlightLoot { get; set; } = true;
    public bool LootCurrentJobOnly { get; set; } = false;
    public List<StorageSnapshot> StorageSnapshots { get; set; } = [];
}
