using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace GearScout;

internal sealed class MeldingWindow : Window
{
    private readonly Plugin plugin;
    private readonly MateriaTargetEditor targets;
    private TrackedSet? editing;
    private GearPiece? editingPiece;
    private Guid selectedId;
    private readonly Dictionary<string, string> selections = [];
    private List<PieceMeldPlan> plans = [];
    private MeldBudget? budget;
    private IReadOnlyCollection<OwnedItem>? lastOwned;
    private TrackedSet? lastSet;
    private int confidenceIndex = 1;
    private static readonly double[] Confidence = [0.90, 0.95, 0.99];
    private int materialLimit;
    private bool limitInitialized;
    private bool dirty = true;
    private string error = "";
    public MeldingWindow(Plugin plugin) : base("GearScout — Melding planner###GearScoutMelding", ImGuiWindowFlags.NoResize)
    {
        this.plugin = plugin; targets = new(plugin);
        Size = new(1100, 760); SizeCondition = ImGuiCond.Always;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new(1100, 760), MaximumSize = new(1100, 760) };
    }
    public void Open(TrackedSet set)
    {
        if (set.IsGlam) return;
        if (selectedId != set.Id) { plugin.MeldingGuide.Stop(); selections.Clear(); limitInitialized = false; }
        selectedId = set.Id; dirty = true; error = ""; IsOpen = true;
    }

    private static string Key(OwnedItem copy, int index) => copy.IsLive ? copy.CopyKey : $"saved:{copy.Location}:{index}";
    private IEnumerable<(OwnedItem Copy, string Key)> Copies(uint id) => (plugin.Owned ?? []).Select((i, n) => (Copy: i, Key: Key(i, n))).Where(i => i.Copy.ItemId == id);
    private static string Label(OwnedItem copy) => $"{copy.Location} / {(copy.HighQuality == null ? "quality unknown" : copy.HighQuality.Value ? "HQ" : "NQ")}" +
        (copy.IsLive ? $" / slot {copy.InventorySlot + 1}" : " / saved");

    private void Refresh(TrackedSet set)
    {
        if (!dirty && ReferenceEquals(lastOwned, plugin.Owned) && ReferenceEquals(lastSet, set)) return;
        if (lastSet != null && !ReferenceEquals(lastSet, set) && plugin.MeldingGuide.Running)
            plugin.MeldingGuide.Stop("Guidance stopped: target set changed. Review the new plan.");
        dirty = false; lastOwned = plugin.Owned; lastSet = set;
        plugin.MeldingData.Load();
        var suggested = GearMatcher.Match(set.Pieces, plugin.Owned ?? []);
        plans = [];
        foreach (var piece in set.Pieces)
        {
            var copies = Copies(piece.ItemId).ToList();
            selections.TryGetValue(piece.Slot, out var key);
            var copy = copies.FirstOrDefault(c => c.Key == key).Copy;
            // Once a live copy is explicitly selected, never silently replace a moved/missing one.
            if (key == null)
            {
                copy = suggested.FirstOrDefault(s => ReferenceEquals(s.Piece, piece))?.Match;
                if (copy != null)
                {
                    var choice = copies.FirstOrDefault(c => ReferenceEquals(c.Copy, copy));
                    if (choice.Key != null) selections[piece.Slot] = choice.Key;
                }
            }
            var rules = copy?.HighQuality is { } hq ? plugin.MeldingData.Rules(piece.ItemId, hq) : null;
            plans.Add(MeldPlanner.Build(piece, copy, rules, plugin.MeldingData.Materia));
        }
        foreach (var group in plans.Where(p => p.Copy?.IsLive == true).GroupBy(p => p.Copy!.CopyKey).Where(g => g.Count() > 1))
            foreach (var plan in group) plan.Errors.Add("This physical copy is selected in more than one target slot. Choose a separate copy.");
        budget = MeldProbability.Budget(plans, plugin.Owned ?? [], Confidence[confidenceIndex]);
        if (!limitInitialized) { materialLimit = budget.Items.Sum(i => i.Budget); limitInitialized = true; }
    }

    public override void Draw()
    {
        var set = plugin.Config.Sets.FirstOrDefault(s => s.Id == selectedId && !s.IsGlam);
        if (set == null) { plugin.MeldingGuide.Stop("Guidance stopped: set removed."); ImGui.TextUnformatted("Select a normal set from the checklist."); return; }
        if (plugin.Owned == null) { ImGui.TextUnformatted("Waiting for complete live inventory."); return; }
        Refresh(set);
        if (plugin.MeldingData.Error != null) ImGui.TextWrapped("Game-data error: " + plugin.MeldingData.Error);
        ImGui.TextUnformatted($"MELDING PLAN / {set.Name} ({set.Job})");
        ImGui.TextWrapped("Select the exact gear copy for each slot. Edit targets for imported or local sets; imported changes save as a local copy. Retrievals remove bottom-first.");
        ImGui.BeginDisabled(plugin.MeldingGuide.Running);
        ImGui.SetNextItemWidth(110);
        if (ImGui.Combo("Whole-plan budget", ref confidenceIndex, new[] { "90%", "95%", "99%" }, 3)) dirty = true;
        ImGui.SameLine(); if (ImGui.SmallButton("Re-select gear copies")) { selections.Clear(); limitInitialized = false; dirty = true; }
        ImGui.EndDisabled();
        if (budget != null)
        {
            ImGui.TextUnformatted($"Chance with bag stock, before refunds: {budget.CompletionChance:P1} {(budget.CompletePlan ? "for this plan" : "for valid pieces only — incomplete plan")}");
            ImGui.TextWrapped("Budgets combine all planned melds and exclude potential retrieval refunds. Separate material-type budgets jointly meet the selected confidence. Saved retainer stock must be withdrawn first.");
        }
        if (ImGui.BeginChild("meldContents", new Vector2(0, 405), true))
        {
            if (ImGui.BeginTable("meldPieces", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Gear / copy", ImGuiTableColumnFlags.WidthFixed, 370);
                ImGui.TableSetupColumn("Plan / validation", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("##target", ImGuiTableColumnFlags.WidthFixed, 95);
                ImGui.TableHeadersRow();
                foreach (var plan in plans)
                {
                    ImGui.PushID(plan.Target.Slot);
                    ImGui.TableNextRow(); ImGui.TableNextColumn();
                    ImGui.TextUnformatted($"{GearSearch.SlotName(plan.Target.Slot)} — {plugin.ItemName(plan.Target.ItemId)}");
                    if (plan.Copy?.HighQuality is { } quality && plugin.MeldingData.Rules(plan.Target.ItemId, quality) is { } rules)
                        ImGui.TextUnformatted($"{rules.GuaranteedSlots} guaranteed / advanced {(rules.Advanced ? "allowed" : "forbidden")} / i{rules.ItemLevel}");
                    ImGui.BeginDisabled(plugin.MeldingGuide.Running);
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.BeginCombo("##copy", plan.Copy == null ? "Choose a copy..." : Label(plan.Copy)))
                    {
                        foreach (var c in Copies(plan.Target.ItemId))
                            if (ImGui.Selectable($"{Label(c.Copy)}##{c.Key}")) { selections[plan.Target.Slot] = c.Key; dirty = true; }
                        ImGui.EndCombo();
                    }
                    ImGui.EndDisabled();
                    if (plan.Copy?.LastCheckedUtc is { } time) ImGui.TextWrapped($"Saved check: {time.ToLocalTime():g}");
                    ImGui.TableNextColumn();
                    foreach (var issue in plan.Errors) ImGui.TextColored(new Vector4(1, 0.55f, 0.3f, 1), issue);
                    foreach (var warning in plan.Warnings) ImGui.TextWrapped(warning);
                    if (plan.Valid)
                    {
                        ImGui.TextUnformatted($"Keep first {plan.Preserved} meld(s). {plan.Actions.Count} action(s).");
                        foreach (var action in plan.Actions)
                            ImGui.TextWrapped($"{action.Kind} slot {action.Position + 1}: {plugin.ItemName(action.MateriaId)} — {(action.Kind == MeldActionKind.Retrieve ? "recovery" : "success")} {action.Chance:P0}");
                    }
                    ImGui.TableNextColumn();
                    ImGui.BeginDisabled(plugin.MeldingGuide.Running);
                    if (ImGui.SmallButton("Edit targets"))
                    {
                        editing = GearSearch.Draft(set);
                        editingPiece = editing.Pieces.First(p => p.Slot == plan.Target.Slot);
                    }
                    ImGui.EndDisabled();
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
            ImGui.Separator();
            if (budget != null && ImGui.BeginTable("materiaBudget", 7, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            {
                foreach (var title in new[] { "Materia", "Bags", "Retainers", "Minimum", "Average", "Budget", "Get more" }) ImGui.TableSetupColumn(title);
                ImGui.TableHeadersRow();
                foreach (var row in budget.Items)
                {
                    ImGui.TableNextRow();
                    foreach (var text in new[] { plugin.ItemName(row.MateriaId), row.Bags.ToString(), row.Retainers.ToString(), row.Minimum.ToString(), row.Average.ToString("F1"), row.Budget.ToString(), row.Additional.ToString() })
                    { ImGui.TableNextColumn(); ImGui.TextWrapped(text); }
                }
                ImGui.EndTable();
            }
            var refunds = plans.Where(p => p.Valid).SelectMany(p => p.Actions).Where(a => a.Kind == MeldActionKind.Retrieve).GroupBy(a => a.MateriaId);
            foreach (var refund in refunds) ImGui.TextWrapped($"Potential recovery: {plugin.ItemName(refund.Key)} — remove {refund.Count()}, average returned {refund.Sum(a => a.Chance):F1}. Not counted as stock until retrieved.");
        }
        ImGui.EndChild();
        DrawGuide();
        if (!string.IsNullOrEmpty(error)) ImGui.TextWrapped(error);
        if (editingPiece != null) { targets.Open(editingPiece); editingPiece = null; }
        targets.Draw();
        if (targets.Changed && editing != null)
        {
            try { plugin.SaveLocalSet(editing); selectedId = editing.Id; dirty = true; limitInitialized = false; editing = null; error = ""; }
            catch (Exception ex) { error = ex.Message; }
        }
    }

    private void DrawGuide()
    {
        var guide = plugin.MeldingGuide;
        ImGui.TextWrapped(guide.Status);
        if (guide.Running)
        {
            if (ImGui.Button("Stop guidance")) guide.Stop();
            ImGui.SameLine(); ImGui.TextUnformatted($"{guide.Completed}/{guide.Total} actions; {guide.Consumed}/{guide.Limit} materia consumed");
            if (guide.Current is { } next)
                ImGui.TextWrapped($"NEXT: {next.Action.Kind} {plugin.ItemName(next.Action.MateriaId)} on {plugin.ItemName(next.Copy.ItemId)} / {Label(next.Copy)} / materia slot {next.Action.Position + 1}.");
        }
        else
        {
            ImGui.SetNextItemWidth(100); ImGui.InputInt("Material limit (all types combined)", ref materialLimit);
            if (ImGui.Button("Use confidence budget")) materialLimit = budget?.Items.Sum(i => i.Budget) ?? 0;
            ImGui.SameLine();
            ImGui.BeginDisabled(plans.Count == 0 || plans.Any(p => !p.Valid || p.Copy?.IsLive != true) || materialLimit < 0);
            if (ImGui.Button("Start guidance"))
            {
                try { guide.Start(plugin.CharacterId, plans, plugin.Owned!, budget!.Items.ToDictionary(i => i.MateriaId, _ => materialLimit), materialLimit, Environment.TickCount64); }
                catch (Exception ex) { error = ex.Message; }
            }
            ImGui.EndDisabled();
        }
        if (ImGui.Button("Open melding window")) plugin.RequestMeldingWindow();
        ImGui.SameLine(); ImGui.TextUnformatted("Retrieve through the gear's in-game context menu.");
        ImGui.TextWrapped("Perform each action in the game's dialogs. Guidance stops at a limit or an unexpected change; it cannot prevent manual spending or cancel the game's batch melding. Use single attempts to follow the limits.");
    }

    public override void OnClose() => plugin.MeldingGuide.Stop();
}
