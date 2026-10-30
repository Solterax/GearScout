using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using AddonRow = Lumina.Excel.Sheets.Addon;

namespace GearScout;

internal sealed unsafe class RetainerScanController(Plugin plugin)
{
    public RetainerScanSession Session { get; } = new();
    private bool startRequested;
    private DateTimeOffset inventoryOpenedAt;
    public bool CanStart => !plugin.AutoMelding.Running && Plugin.GameGui.GetAddonByName("RetainerList").IsVisible && Plugin.GameGui.GetAddonByName("RetainerList").IsReady &&
        !Plugin.GameGui.GetAddonByName("SelectString").IsVisible &&
        !Plugin.GameGui.GetAddonByName("InventoryRetainer").IsVisible && !Plugin.GameGui.GetAddonByName("InventoryRetainerLarge").IsVisible;
    public void RequestStart() => startRequested = true;
    public void Stop() { startRequested = false; Session.Stop(); }

    public void Update(bool ready)
    {
        try
        {
            if (startRequested)
            {
                startRequested = false;
                if (!ready || !CanStart) { Session.Stop("Open the summoning bell's retainer list first."); return; }
                Session.Start(ReadRoster().Select(r => r.Target), plugin.CharacterId, Environment.TickCount64);
            }
            if (!Session.Running) return;
            var manager = RetainerManager.Instance();
            var activeId = manager == null ? 0 : manager->LastSelectedRetainerId;
            var target = Session.Current!;
            var saved = !plugin.StorageSavePending && plugin.Config.StorageSnapshots.Any(s =>
                s.CharacterId == plugin.CharacterId && s.Key == $"retainer:{target.Id}:bags" && s.CheckedAtUtc >= inventoryOpenedAt);
            var action = Session.Tick(new(plugin.CharacterId, ready, CanStart,
                Open("SelectString"), InventoryAddon() != null, activeId, saved,
                Open("SelectYesno") || Open("RetainerTaskResult") || Open("RetainerTaskAsk")), Environment.TickCount64);
            switch (action)
            {
                case RetainerScanAction.SelectRetainer:
                    var entry = ReadRoster().FirstOrDefault(r => r.Target.Id == target.Id);
                    if (entry == null) throw new InvalidOperationException("The selected retainer is no longer available.");
                    var addon = Plugin.GameGui.GetAddonByName<AddonRetainerList>("RetainerList");
                    Span<AtkValue> values = stackalloc AtkValue[4];
                    values.Clear();
                    values[0].Type = AtkValueType.Int; values[0].Int = 2;
                    values[1].Type = AtkValueType.UInt; values[1].UInt = (uint)entry.Index;
                    fixed (AtkValue* args = values) addon->FireCallback(4, args, true);
                    break;
                case RetainerScanAction.OpenInventory:
                    inventoryOpenedAt = DateTimeOffset.UtcNow;
                    SelectMenu(2378); // Localized "Entrust or withdraw items".
                    break;
                case RetainerScanAction.CloseInventory:
                    var inventory = InventoryAddon();
                    if (inventory == null) throw new InvalidOperationException("Retainer inventory closed unexpectedly.");
                    var agent = AgentRetainer.Instance();
                    if (agent == null || !agent->IsAgentActive()) throw new InvalidOperationException("Retainer inventory agent is not active.");
                    agent->Hide();
                    break;
                case RetainerScanAction.QuitRetainer:
                    SelectMenu(2383); // Localized "Quit"; no venture or transfer actions.
                    break;
            }
        }
        catch (Exception ex)
        {
            Session.Stop("Retainer scan stopped: " + ex.Message);
            Plugin.Log.Warning(ex, "Retainer scan stopped");
        }
    }

    private sealed record RosterEntry(int Index, RetainerScanTarget Target);
    private static List<RosterEntry> ReadRoster()
    {
        var result = new List<RosterEntry>();
        var addon = Plugin.GameGui.GetAddonByName<AddonRetainerList>("RetainerList");
        var manager = RetainerManager.Instance();
        if (addon == null || !addon->IsReady || !addon->IsVisible || manager == null || !manager->IsReady) return result;
        // RetainerList exposes ten 10-value rows starting at value 3: name at +0, active at +8.
        for (var index = 0; index < 10; index++)
        {
            var offset = 3 + index * 10;
            if (offset + 8 >= addon->AtkValuesCount) break;
            var active = addon->AtkValues[offset + 8];
            var name = addon->AtkValues[offset];
            if (active.Type != AtkValueType.Bool || active.Byte == 0 ||
                name.Type is not (AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString) || name.String.Value == null) continue;
            var text = SeString.Parse(name.String).TextValue.Trim();
            foreach (ref readonly var retainer in manager->Retainers)
                if (retainer.RetainerId != 0 && string.Equals(retainer.NameString, text, StringComparison.Ordinal))
                {
                    result.Add(new(index, new(retainer.RetainerId, text)));
                    break;
                }
        }
        return result;
    }

    private static void SelectMenu(uint textRow)
    {
        var addon = Plugin.GameGui.GetAddonByName<AddonSelectString>("SelectString");
        if (addon == null || !addon->IsVisible || !addon->IsReady) throw new InvalidOperationException("Retainer menu is not ready.");
        var template = Plugin.Data.GetExcelSheet<AddonRow>().GetRow(textRow).Text.Data.ToArray();
        var menu = &addon->PopupMenu.PopupMenu;
        if (menu->EntryNames == null || menu->EntryCount is < 1 or > 32) throw new InvalidOperationException("Unexpected retainer menu.");
        var entries = new string[menu->EntryCount];
        for (var i = 0; i < entries.Length; i++)
            entries[i] = menu->EntryNames[i].Value == null ? "" : SeString.Parse(menu->EntryNames[i].Value).TextValue;
        var index = RetainerMenuMatching.FindEntry(template, entries);
        if (index < 0) throw new InvalidOperationException($"Retainer menu entry for Addon row {textRow} was missing or ambiguous.");
        if (menu->List != null && menu->List->GetItemDisabledState(index)) throw new InvalidOperationException("Retainer menu entry is disabled.");
        AtkValue value = new() { Type = AtkValueType.Int, Int = index };
        addon->FireCallback(1, &value, true);
    }

    private static bool Open(string name) => Plugin.GameGui.GetAddonByName(name).IsReady && Plugin.GameGui.GetAddonByName(name).IsVisible;
    private static AtkUnitBase* InventoryAddon()
    {
        foreach (var name in new[] { "InventoryRetainer", "InventoryRetainerLarge" })
        {
            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name);
            if (addon != null && addon->IsReady && addon->IsVisible) return addon;
        }
        return null;
    }
}
