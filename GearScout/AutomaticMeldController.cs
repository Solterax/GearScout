using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MateriaRow = Lumina.Excel.Sheets.Materia;

namespace GearScout;

internal sealed unsafe class AutomaticMeldController
{
    private readonly Plugin plugin;
    private List<(GearPiece Piece, uint Id, string Slot, uint[] Target)> boundTargets = [];
    public AutomaticMeldSession Session { get; }
    public bool Running => Session.Running;
    public AutomaticMeldController(Plugin plugin) { this.plugin = plugin; Session = new(plugin.MeldingGuide); }
    public void Start(IReadOnlyList<PieceMeldPlan> plans, IReadOnlyDictionary<uint, int> limits, int totalLimit)
    {
        if (Open("MateriaAttachDialog") || Open("MateriaRetrieveDialog") || Open("SelectYesno") || Busy())
            throw new InvalidOperationException("Close confirmation dialogs and finish the current action before starting automatic melding.");
        if (plugin.RetainerScan.Session.Running) throw new InvalidOperationException("Stop the retainer scan before starting automatic melding.");
        Session.Start(plugin.CharacterId, plans, plugin.Owned ?? throw new InvalidOperationException("Inventory is unavailable."), limits, totalLimit, Environment.TickCount64);
        boundTargets = plans.Select(p => (p.Target, p.Target.ItemId, p.Target.Slot, p.Target.Materia!.ToArray())).ToList();
    }
    public void Stop() => Session.Stop();

    public void Update(bool ready)
    {
        if (!Running) return;
        try
        {
            if (boundTargets.Any(t => !plugin.Config.Sets.Any(s => s.Pieces.Any(p => ReferenceEquals(p, t.Piece))) ||
                t.Piece.ItemId != t.Id || t.Piece.Slot != t.Slot || t.Piece.Materia == null || !t.Piece.Materia.SequenceEqual(t.Target)))
                throw new InvalidOperationException("A target set was removed or edited. Review the updated plan before restarting.");
            var step = plugin.MeldingGuide.Current!;
            var agent = AgentMateriaAttach.Instance();
            var readable = agent != null && agent->IsAgentActive() && agent->Data != null;
            if (readable && (agent->ItemCount is < 0 or > 140 || agent->MateriaCount is < 0 or > 140))
                throw new InvalidOperationException("Unexpected native materia list size.");
            var category = Category(step.Copy.Container);
            var target = Target(step.Copy, false);
            var gearMatches = readable && SelectedItem(agent, false) == target && target != null;
            var selectedMateria = readable ? SelectedItem(agent, true) : null;
            var materiaMatches = selectedMateria != null && LootMatcher.BaseItemId(selectedMateria->ItemId) == step.Action.MateriaId &&
                (int)selectedMateria->Container is >= 0 and <= 3 && selectedMateria->Quantity > 0;
            var attach = Addon("MateriaAttachDialog"); var retrieve = Addon("MateriaRetrieveDialog");
            var dialog = step.Action.Kind == MeldActionKind.Meld ? attach : retrieve;
            var expectedDialog = dialog != null && (step.Action.Kind == MeldActionKind.Meld ? retrieve == null : attach == null);
            var rateMatches = step.Action.Kind == MeldActionKind.Retrieve || attach != null && attach->AtkValuesCount > 41 &&
                attach->AtkValues[41].Type == AtkValueType.Int && attach->AtkValues[41].Int == (int)Math.Round(step.Action.Chance * 100);
            var view = new AutoMeldView(plugin.CharacterId, ready, plugin.Owned, Open("MateriaAttach"),
                readable && agent->UpdateState == 0, readable && agent->Category == category, gearMatches, materiaMatches,
                attach != null || retrieve != null, expectedDialog, rateMatches, Busy(),
                Open("SelectYesno") || Open("ContextMenu") || plugin.RetainerScan.Session.Running);
            var command = Session.Tick(view, Environment.TickCount64);
            if (command == AutoMeldCommand.None) return;
            switch (command)
            {
                case AutoMeldCommand.OpenWindow:
                    plugin.RequestMeldingWindow();
                    return;
                case AutoMeldCommand.SelectCategory:
                    Send(agent, 0, [0, (int)category]);
                    return;
                case AutoMeldCommand.SelectGear:
                    var index = Find(agent, target, false);
                    if (index < 0) throw new InvalidOperationException("Exact gear copy was not found in the game's melding list.");
                    Send(agent, 0, [1, index, 1, 0]);
                    return;
                case AutoMeldCommand.RequestRetrieve:
                    ValidateAction(step);
                    if (InventoryManager.Instance()->GetEmptySlotsInBag() == 0)
                        throw new InvalidOperationException("Free an inventory slot before retrieving materia.");
                    Send(agent, 4, [0, 1, 0, 0, 0]);
                    return;
                case AutoMeldCommand.SelectMateria:
                    ValidateAction(step);
                    var materiaIndex = FindMateria(agent, step.Action.MateriaId);
                    if (materiaIndex < 0) throw new InvalidOperationException("Planned materia is not available in the native melding list.");
                    Send(agent, 0, [2, materiaIndex, 1, 0]);
                    return;
                case AutoMeldCommand.Confirm:
                    ValidateAction(step);
                    if (!gearMatches || !expectedDialog || !rateMatches || step.Action.Kind == MeldActionKind.Meld && !materiaMatches)
                        throw new InvalidOperationException("Native confirmation identity changed.");
                    if (step.Action.Kind == MeldActionKind.Meld)
                    {
                        var button = dialog->GetComponentButtonById(35);
                        if (button == null || !button->IsEnabled) throw new InvalidOperationException("Native Meld button is not enabled.");
                        Callback(dialog, [0, 0, 1]); // One attempt, never native batch/repeat.
                    }
                    else Callback(dialog, [0]);
                    return;
            }
        }
        catch (Exception ex)
        {
            Session.Stop("Automatic melding stopped: " + ex.Message);
            Plugin.Log.Warning(ex, "Automatic melding stopped");
        }
    }

    private void ValidateAction(MeldGuideStep step)
    {
        var target = Target(step.Copy, true)!;
        var rules = plugin.MeldingData.Rules(step.Copy.ItemId, step.Copy.HighQuality == true);
        if (rules == null || rules.GuaranteedSlots == 0) throw new InvalidOperationException("This item cannot be melded.");
        var melds = new List<uint>();
        for (var n = 0; n < 5; n++)
        {
            var type = target->GetMateriaId((byte)n); if (type == 0) continue;
            if (!Plugin.Data.GetExcelSheet<MateriaRow>().TryGetRow(type, out var row) || target->GetMateriaGrade((byte)n) >= row.Item.Count)
                throw new InvalidOperationException("Current native materia is unknown.");
            melds.Add(row.Item[target->GetMateriaGrade((byte)n)].RowId);
        }
        if (!melds.SequenceEqual(step.Action.Before)) throw new InvalidOperationException("Native gear melds changed before confirmation.");
        if (step.Action.Kind == MeldActionKind.Meld && (!plugin.MeldingData.Materia.TryGetValue(step.Action.MateriaId, out var m) ||
            MeldPlanner.Rate(rules, m, step.Copy.HighQuality == true, step.Action.Position) <= 0))
            throw new InvalidOperationException("The planned meld is no longer allowed.");
    }

    private static InventoryItem* Target(OwnedItem copy, bool required)
    {
        var manager = InventoryManager.Instance();
        var container = manager == null ? null : manager->GetInventoryContainer((InventoryType)copy.Container);
        var item = container == null || !container->IsLoaded || copy.InventorySlot < 0 || copy.InventorySlot >= container->Size ? null : container->GetInventorySlot(copy.InventorySlot);
        if (item != null && !item->IsSymbolic && LootMatcher.BaseItemId(item->ItemId) == copy.ItemId &&
            ((item->Flags & InventoryItem.ItemFlags.HighQuality) != 0) == copy.HighQuality) return item;
        if (required) throw new InvalidOperationException("Exact native gear copy moved or changed.");
        return null;
    }

    private static InventoryItem* SelectedItem(AgentMateriaAttach* agent, bool materia)
    {
        var index = materia ? agent->SelectedMateriaIndex : agent->SelectedItemIndex;
        var count = materia ? agent->MateriaCount : agent->ItemCount;
        if (index < 0 || index >= count || agent->Data == null) return null;
        var list = materia ? agent->Data->MateriaArraySorted : agent->Data->ItemArraySorted;
        return list == null || list[index] == null ? null : list[index]->Item;
    }
    private static int Find(AgentMateriaAttach* agent, InventoryItem* item, bool materia)
    {
        if (agent == null || agent->Data == null || item == null) return -1;
        var list = materia ? agent->Data->MateriaArraySorted : agent->Data->ItemArraySorted;
        var count = materia ? agent->MateriaCount : agent->ItemCount;
        if (list == null || count is < 0 or > 140) return -1;
        for (var i = 0; i < count; i++) if (list[i] != null && list[i]->Item == item) return i;
        return -1;
    }
    private static int FindMateria(AgentMateriaAttach* agent, uint id)
    {
        if (agent == null || agent->Data == null || agent->Data->MateriaArraySorted == null || agent->MateriaCount is < 0 or > 140) return -1;
        for (var n = 0; n < agent->MateriaCount; n++)
        {
            var entry = agent->Data->MateriaArraySorted[n];
            var item = entry == null ? null : entry->Item;
            if (item != null && LootMatcher.BaseItemId(item->ItemId) == id && (int)item->Container is >= 0 and <= 3 && item->Quantity > 0) return n;
        }
        return -1;
    }
    private static AgentMateriaAttach.FilterCategory Category(int container) => (InventoryType)container switch
    {
        InventoryType.Inventory1 or InventoryType.Inventory2 or InventoryType.Inventory3 or InventoryType.Inventory4 => AgentMateriaAttach.FilterCategory.Inventory,
        InventoryType.EquippedItems => AgentMateriaAttach.FilterCategory.Equipped,
        InventoryType.ArmoryMainHand or InventoryType.ArmoryOffHand => AgentMateriaAttach.FilterCategory.ArmouryWeapon,
        InventoryType.ArmoryHead or InventoryType.ArmoryBody or InventoryType.ArmoryHands => AgentMateriaAttach.FilterCategory.ArmouryHeadBodyHands,
        InventoryType.ArmoryLegs or InventoryType.ArmoryFeets => AgentMateriaAttach.FilterCategory.ArmouryLegsFeet,
        InventoryType.ArmoryEar or InventoryType.ArmoryNeck => AgentMateriaAttach.FilterCategory.ArmouryNeckEars,
        InventoryType.ArmoryWrist or InventoryType.ArmoryRings => AgentMateriaAttach.FilterCategory.ArmouryWristRing,
        _ => throw new InvalidOperationException("Unsupported gear inventory for automatic melding."),
    };
    private static bool Busy() => Plugin.Condition[ConditionFlag.MeldingMateria] || Plugin.Condition[ConditionFlag.Occupied39];
    private static bool Open(string name) => Addon(name) != null;
    private static AtkUnitBase* Addon(string name)
    {
        var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name);
        return addon != null && addon->IsVisible && addon->IsReady ? addon : null;
    }
    private static void Send(AgentMateriaAttach* agent, ulong kind, int[] args)
    {
        if (agent == null || !agent->IsAgentActive()) throw new InvalidOperationException("Melding agent is unavailable.");
        var values = stackalloc AtkValue[args.Length];
        for (var i = 0; i < args.Length; i++) values[i] = new AtkValue { Type = AtkValueType.Int, Int = args[i] };
        AtkValue result = new(); agent->ReceiveEvent(&result, values, (uint)args.Length, kind);
    }
    private static void Callback(AtkUnitBase* addon, int[] args)
    {
        if (addon == null || !addon->IsReady || !addon->IsVisible) throw new InvalidOperationException("Confirmation disappeared.");
        var values = stackalloc AtkValue[args.Length];
        for (var i = 0; i < args.Length; i++) values[i] = new AtkValue { Type = AtkValueType.Int, Int = args[i] };
        addon->FireCallback((uint)args.Length, values, true);
    }
}
