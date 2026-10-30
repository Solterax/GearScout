using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace GearScout;

internal sealed unsafe class EquipSetController(Plugin plugin)
{
    public EquipSetSession Session { get; } = new();
    public bool Running => Session.Running;
    private TrackedSet? sourceSet;
    private string fingerprint = "";
    private bool reported;

    public void Start(TrackedSet set)
    {
        if (plugin.Owned == null) throw new InvalidOperationException("Wait for complete character inventory.");
        if (plugin.AutoMelding.Running || plugin.RetainerScan.Session.Running || Blocked())
            throw new InvalidOperationException("Finish the current action, combat, retainer scan or automatic melding first.");
        Session.Start(set, plugin.CharacterId, plugin.CurrentJob, Environment.TickCount64);
        sourceSet = set; fingerprint = Fingerprint(set); reported = false;
    }
    public void Stop(string reason = "Stopped by you.") { Session.Stop(reason); Report(); }

    public void Update(bool ready)
    {
        if (!Running) { Report(); return; }
        if (sourceSet == null || !plugin.Config.Sets.Any(s => ReferenceEquals(s, sourceSet)) || Fingerprint(sourceSet) != fingerprint)
        { Stop("The target set was edited or removed."); return; }
        try
        {
            var request = Session.Tick(plugin.CharacterId, plugin.CurrentJob, ready,
                Blocked() || plugin.AutoMelding.Running || plugin.RetainerScan.Session.Running,
                plugin.Owned, Environment.TickCount64);
            if (request != null) Equip(request);
        }
        catch (Exception ex)
        {
            if (Session.Pending != null) Session.Reject(ex.Message, Environment.TickCount64);
            else Session.Stop(ex.Message);
            Plugin.Log.Warning(ex, "Could not equip a set item");
        }
        Report();
    }

    private static void Equip(EquipRequest request)
    {
        var source = request.Source;
        var manager = InventoryManager.Instance();
        var container = manager == null ? null : manager->GetInventoryContainer((InventoryType)source.Container);
        var item = container == null || !container->IsLoaded || source.InventorySlot < 0 || source.InventorySlot >= container->Size ? null : container->GetInventorySlot(source.InventorySlot);
        if (item == null || item->IsSymbolic || LootMatcher.BaseItemId(item->ItemId) != request.Piece.ItemId ||
            ((item->Flags & InventoryItem.ItemFlags.HighQuality) != 0) != source.HighQuality)
            throw new InvalidOperationException("The selected inventory copy moved or changed before equipping.");
        if (!Plugin.Data.GetExcelSheet<Item>().TryGetRow(request.Piece.ItemId, out var row) || !row.EquipSlotCategory.IsValid)
            throw new InvalidOperationException("Item equip rules are unavailable.");
        var category = row.EquipSlotCategory.Value;
        var fits = request.Piece.Slot switch
        {
            "Weapon" => category.MainHand > 0, "OffHand" => category.OffHand > 0, "Head" => category.Head > 0,
            "Body" => category.Body > 0, "Hand" => category.Gloves > 0, "Legs" => category.Legs > 0,
            "Feet" => category.Feet > 0, "Ears" => category.Ears > 0, "Neck" => category.Neck > 0,
            "Wrist" => category.Wrists > 0, "RingLeft" or "RingRight" => category.FingerL > 0 || category.FingerR > 0, _ => false,
        };
        if (!fits) throw new InvalidOperationException("This item does not fit the requested gear slot.");
        if (source.Materia != null)
        {
            var current = new List<uint>();
            for (var n = 0; n < 5; n++)
            {
                var type = item->GetMateriaId((byte)n); if (type == 0) continue;
                if (!Plugin.Data.GetExcelSheet<Materia>().TryGetRow(type, out var m) || item->GetMateriaGrade((byte)n) >= m.Item.Count)
                    throw new InvalidOperationException("Current materia changed or became unavailable before equipping.");
                current.Add(m.Item[item->GetMateriaGrade((byte)n)].RowId);
            }
            if (!current.SequenceEqual(source.Materia)) throw new InvalidOperationException("The selected copy's materia changed before equipping.");
        }
        if (row.LevelEquip > Plugin.Player.Level) throw new InvalidOperationException($"Requires level {row.LevelEquip}; your current job is level {Plugin.Player.Level}.");
        var allowed = InventoryManager.CanEquip(row.RowId, (byte)Plugin.Player.Race.RowId, (byte)Plugin.Player.Sex,
            (ushort)Plugin.Player.Level, (byte)Plugin.Player.ClassJob.RowId, (byte)Plugin.Player.GrandCompany.RowId, 0, null);
        if (allowed != 0) throw new InvalidOperationException($"The game does not allow this item for your character/current job (reason {allowed}).");
        // Same native swap used when dragging a specific inventory/Armoury copy onto a gear slot.
        manager->MoveItemSlot((InventoryType)source.Container, checked((ushort)source.InventorySlot), InventoryType.EquippedItems,
            checked((ushort)request.Destination), true);
    }

    private void Report()
    {
        if (reported || Running || Session.Results.Count == 0) return;
        reported = true;
        Plugin.Chat.Print(Session.Status, "GearScout");
        foreach (var result in Session.Results.Where(r => r.Kind is not (EquipResultKind.Equipped or EquipResultKind.AlreadyEquipped)))
            Plugin.Chat.Print($"{GearSearch.SlotName(result.Piece.Slot)} — {plugin.ItemName(result.Piece.ItemId)}: {result.Details}", "GearScout");
    }
    private static bool Blocked() => Plugin.Condition[ConditionFlag.InCombat] || Plugin.Condition[ConditionFlag.Occupied39] ||
        Plugin.Condition[ConditionFlag.MeldingMateria] || Plugin.GameGui.GetAddonByName("SelectYesno").IsVisible ||
        Plugin.GameGui.GetAddonByName("ContextMenu").IsVisible;
    private static string Fingerprint(TrackedSet set) => set.Job + "|" + string.Join(';', set.Pieces.Select(p =>
        $"{p.Slot}:{p.ItemId}:" + (p.Materia == null ? "?" : string.Join(',', p.Materia))));
}
