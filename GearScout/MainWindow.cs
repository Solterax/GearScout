using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace GearScout;

internal sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private string link = "";
    private bool missingOnly;
    private static readonly Vector4 Green = new(0.42f, 0.88f, 0.62f, 1);
    private static readonly Vector4 Amber = new(1, 0.73f, 0.35f, 1);
    private static readonly Vector4 Muted = new(0.65f, 0.7f, 0.76f, 1);

    public MainWindow(Plugin plugin) : base("GearScout — XIVGear tracker###GearScoutMain")
    {
        this.plugin = plugin;
        Size = new Vector2(820, 610);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(600, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("XIVGEAR  /  GEAR CHECKLIST");
        ImGui.TextColored(Muted, "Equipped gear + bags + Armoury Chest. Updates every second.");
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-95);
        ImGui.InputTextWithHint("##link", "Paste an xivgear.app gear set or sheet link", ref link, 4096);
        ImGui.SameLine();
        ImGui.BeginDisabled(plugin.Importing || string.IsNullOrWhiteSpace(link));
        if (ImGui.Button(plugin.Importing ? "Loading" : "Import")) plugin.StartImport(link);
        ImGui.EndDisabled();
        if (!string.IsNullOrEmpty(plugin.Message))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, plugin.MessageIsError ? Amber : Muted);
            ImGui.TextWrapped(plugin.Message);
            ImGui.PopStyleColor();
        }
        ImGui.Separator();

        var job = plugin.CurrentJob;
        if (string.IsNullOrEmpty(job))
        {
            ImGui.TextWrapped("Log in and finish loading to see sets for your current class or job.");
            return;
        }
        ImGui.TextUnformatted($"Current job: {job}");
        var sets = plugin.Config.Sets.Where(s => s.Job == job).ToList();
        if (sets.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextWrapped($"No saved sets for {job}. Import a link for this job. Other jobs' sets appear when you switch jobs.");
            return;
        }

        plugin.Config.SelectedByJob.TryGetValue(job, out var selectedId);
        var selected = sets.FirstOrDefault(s => s.Id == selectedId) ?? sets[0];
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##sets", selected.Name))
        {
            foreach (var set in sets)
            {
                if (ImGui.Selectable($"{set.Name}##{set.Id}", set.Id == selected.Id))
                {
                    plugin.Config.SelectedByJob[job] = set.Id;
                    SaveSafely();
                    selected = set;
                }
                if (set.Id == selected.Id) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.Checkbox("Missing only", ref missingOnly);
        ImGui.SameLine();
        if (ImGui.SmallButton("Copy source link")) ImGui.SetClipboardText(selected.SourceUrl);
        ImGui.SameLine();
        if (ImGui.SmallButton("Remove set")) ImGui.OpenPopup("Remove this set?");
        if (ImGui.BeginPopup("Remove this set?"))
        {
            ImGui.TextUnformatted("Remove the selected saved set?");
            if (ImGui.Button("Remove"))
            {
                plugin.Config.Sets.Remove(selected);
                plugin.Config.SelectedByJob.Remove(job);
                SaveSafely();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        var owned = plugin.Owned;
        if (owned == null)
        {
            ImGui.TextWrapped("Waiting for your character's inventory. Ownership is not yet known.");
            return;
        }
        var statuses = GearMatcher.Match(selected.Pieces, owned);
        var have = statuses.Count(s => s.Match != null);
        ImGui.Spacing();
        ImGui.TextUnformatted($"{have} / {statuses.Count} pieces owned   |   {statuses.Count - have} missing from checked storage");
        ImGui.ProgressBar(statuses.Count == 0 ? 0 : (float)have / statuses.Count, new Vector2(-1, 7), "");
        ImGui.TextColored(Muted, "Item IDs only: materia, HQ quality and relic stats are not checked.");
        ImGui.TextColored(Muted, "Retainers, saddlebag and glamour storage are not searched.");

        if (!ImGui.BeginTable("gear", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH |
                ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY, new Vector2(0, Math.Max(100, ImGui.GetContentRegionAvail().Y)))) return;
        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 125);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();
        foreach (var status in statuses)
        {
            if (missingOnly && status.Match != null) continue;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(SlotName(status.Piece.Slot));
            ImGui.TableNextColumn();
            ImGui.TextWrapped(plugin.ItemName(status.Piece.ItemId));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Item ID: {status.Piece.ItemId}");
            ImGui.TableNextColumn();
            ImGui.TextColored(status.Match != null ? Green : Amber,
                status.Match is { Equipped: true } ? "Equipped" : status.Match != null ? "Owned" : "Missing");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(status.Match?.Location ?? "Not found");
        }
        ImGui.EndTable();
    }

    private void SaveSafely()
    {
        try { plugin.Save(); }
        catch (Exception ex) { plugin.ReportError($"Could not save settings: {ex.Message}"); }
    }

    private static string SlotName(string slot) => slot switch
    {
        "Weapon" => "Main hand", "OffHand" => "Off hand", "Hand" => "Hands", "Ears" => "Earrings",
        "Neck" => "Necklace", "Wrist" => "Bracelets", "RingLeft" => "Left ring", "RingRight" => "Right ring", _ => slot,
    };
}
