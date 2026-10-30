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

    public MainWindow(Plugin plugin) : base($"GearScout v{typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "unknown"} — Gear sets###GearScoutMain")
    {
        this.plugin = plugin;
        Size = new Vector2(1000, 650);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(600, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("GEARSCOUT  /  GEAR SETS");
        ImGui.TextColored(Muted, "Equipped gear + bags + Armoury Chest + saved storage checks.");
        var highlightLoot = plugin.Config.HighlightLoot;
        if (ImGui.Checkbox("Blue glow on missing set items", ref highlightLoot))
        {
            plugin.Config.HighlightLoot = highlightLoot;
            SaveSafely();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Highlights wanted, missing items for ALL saved jobs, including gear you can only Greed.\nIncludes saved retainer, dresser and armoire checks for this character.\nCoffers and exchange tokens are not mapped.");
        ImGui.TextColored(Muted, "Loot checks: all saved jobs");
        var notifyWanted = plugin.Config.NotifyWantedItems;
        if (ImGui.Checkbox("Chat messages for wanted drops and new items", ref notifyWanted))
        {
            plugin.Config.NotifyWantedItems = notifyWanted;
            SaveSafely();
        }
        DrawStorage();
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
        ImGui.SameLine();
        if (ImGui.Button("New set")) plugin.CreateSet();
        ImGui.SameLine();
        if (ImGui.Button("New glam set")) plugin.CreateGlamSet();
        var sets = plugin.Config.Sets.Where(s => GearSearch.VisibleForJob(s, job)).ToList();
        if (sets.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextWrapped($"No saved sets for {job}. Click New set to build one here, or import a link. Other jobs' sets appear when you switch jobs.");
            return;
        }

        plugin.Config.SelectedByJob.TryGetValue(job, out var selectedId);
        var selected = sets.FirstOrDefault(s => s.Id == selectedId) ?? sets[0];
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##sets", (selected.IsGlam ? "[Glam] " : "") + selected.Name))
        {
            foreach (var set in sets)
            {
                if (ImGui.Selectable($"{(set.IsGlam ? "[Glam] " : "")}{set.Name}##{set.Id}", set.Id == selected.Id))
                {
                    plugin.Config.SelectedByJob[job] = set.Id;
                    SaveSafely();
                    selected = set;
                }
                if (set.Id == selected.Id) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        var local = string.IsNullOrWhiteSpace(selected.SourceUrl);
        if (ImGui.SmallButton(local ? "Edit set" : "Edit local copy")) plugin.EditSet(selected);
        ImGui.SameLine();
        ImGui.Checkbox("Needs work only", ref missingOnly);
        ImGui.SameLine();
        ImGui.BeginDisabled(plugin.Importing || local);
        if (ImGui.SmallButton("Refresh from link")) plugin.StartImport(selected.SourceUrl);
        ImGui.SameLine();
        if (ImGui.SmallButton("Copy source link")) ImGui.SetClipboardText(selected.SourceUrl);
        ImGui.EndDisabled();
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

        if (!selected.IsGlam && ImGui.Button("Melding planner")) plugin.OpenMelding(selected);
        if (selected.Pieces.Count == 0) ImGui.TextWrapped("This set is empty. Click Edit set to choose gear for its slots.");
        var owned = plugin.Owned;
        if (owned == null)
        {
            ImGui.TextWrapped("Waiting for your character's inventory. Ownership is not yet known.");
            return;
        }
        IEnumerable<GearPiece> pieces = selected.Pieces;
        if (selected.IsGlam) pieces = pieces.OrderBy(p => Array.IndexOf(GearSearch.Slots, p.Slot));
        var statuses = GearMatcher.Match(pieces, owned, selected.IsGlam);
        var have = statuses.Count(s => s.Match != null);
        var melded = statuses.Count(s => s.Melds is { IsExact: true });
        ImGui.Spacing();
        ImGui.TextUnformatted($"{have} / {statuses.Count} {(selected.IsGlam ? "appearances" : "pieces")} owned   |   {statuses.Count - have} missing from checked storage");
        var plannedMelds = statuses.Count(s => s.Piece.Materia != null);
        if (!selected.IsGlam && plannedMelds > 0) ImGui.TextUnformatted($"{melded} / {plannedMelds} pieces have the planned materia (hover Materia for details)");
        ImGui.ProgressBar(statuses.Count == 0 ? 0 : (float)have / statuses.Count, new Vector2(-1, 7), "");
        ImGui.TextColored(Muted, selected.IsGlam ? "Glam set: all jobs; one owned appearance can be reused across slots." : "Materia type and grade checked; slot order ignored. HQ quality and relic stats are not checked.");
        if (!local && selected.Pieces.Any(p => p.Materia == null))
            ImGui.TextColored(Amber, "Materia targets unavailable for some pieces. Click Refresh from link to update an older import.");
        ImGui.TextColored(Muted, "Retainers, dresser and armoire use last checked contents. Reopen them after changes.");

        if (!ImGui.BeginTable("gear", selected.IsGlam ? 4 : 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH |
                ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY, new Vector2(0, Math.Max(100, ImGui.GetContentRegionAvail().Y)))) return;
        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 90);
        if (!selected.IsGlam) ImGui.TableSetupColumn("Materia", ImGuiTableColumnFlags.WidthFixed, 135);
        ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 125);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();
        foreach (var status in statuses)
        {
            if (missingOnly && status.Match != null && (selected.IsGlam || status.Melds is { IsExact: true } || (local && status.Piece.Materia == null))) continue;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(SlotName(status.Piece.Slot));
            ImGui.TableNextColumn();
            if (ImGui.Selectable($"{plugin.ItemName(status.Piece.ItemId)}##edit-{status.Piece.Slot}-{status.Piece.ItemId}"))
                plugin.EditSet(selected, status.Piece.Slot);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{plugin.ItemName(status.Piece.ItemId)}\nItem ID: {status.Piece.ItemId}\n" +
                (selected.IsGlam ? "Click to add or remove pieces for this slot in the set editor." : "Click to change this slot in the set editor."));
            ImGui.TableNextColumn();
            ImGui.TextColored(status.Match != null ? Green : Amber,
                status.Match is { Equipped: true } ? "Equipped" : status.Match != null ? "Owned" : "Missing");
            if (!selected.IsGlam)
            {
                ImGui.TableNextColumn();
                DrawMateria(status, local);
            }
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(status.Match?.Location ?? "Not found");
            if (status.Match?.LastCheckedUtc is { } checkedAt && ImGui.IsItemHovered())
                ImGui.SetTooltip($"{status.Match.Location}\nLast checked: {checkedAt.ToLocalTime():g}\nSaved locally for this character. Reopen this storage to refresh it.");
        }
        ImGui.EndTable();
    }

    private void DrawStorage()
    {
        if (!ImGui.CollapsingHeader("Saved storage checks")) return;
        ImGui.TextWrapped("Storage saves automatically on opening and after item transfers. To check every retainer, open a summoning bell to its retainer list and start a scan.");
        if (plugin.RetainerScan.Session.Running)
        {
            if (ImGui.Button("Stop retainer scan")) plugin.RetainerScan.Stop();
            ImGui.ProgressBar((float)plugin.RetainerScan.Session.Completed / Math.Max(1, plugin.RetainerScan.Session.Total), new Vector2(-1, 8), "");
        }
        else
        {
            ImGui.BeginDisabled(!plugin.RetainerScan.CanStart);
            if (ImGui.Button("Scan all retainers")) plugin.RetainerScan.RequestStart();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Open the summoning bell's retainer list first. This visits available retainers one at a time, saves their inventories, then returns to the list.");
        }
        ImGui.TextWrapped(plugin.RetainerScan.Session.Status);
        if (ImGui.Button("Scan open storage")) plugin.RequestStorageScan();
        ImGui.TextWrapped(plugin.StorageStatus);
        ImGui.TextColored(Muted, "Last-known contents can become stale. Forget old checks or reopen storage to refresh.");
        var snapshots = plugin.Config.StorageSnapshots.Where(s => s.CharacterId == plugin.CharacterId).OrderBy(s => s.Name).ToArray();
        if (snapshots.Length == 0) ImGui.TextUnformatted("No storage checked for this character yet.");
        if (ImGui.BeginTable("storage", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, snapshots.Length == 0 ? 35 : 155)))
        {
            ImGui.TableSetupColumn("Storage", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Items", ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Last checked", ImGuiTableColumnFlags.WidthFixed, 155);
            ImGui.TableSetupColumn("##action", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableHeadersRow();
            foreach (var snapshot in snapshots)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextWrapped(snapshot.Name);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(snapshot.Items.Sum(i => (long)i.Quantity).ToString());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(snapshot.CheckedAtUtc.ToLocalTime().ToString("g"));
                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"Forget##{snapshot.Key}")) plugin.ForgetStorage(snapshot.Key);
            }
            ImGui.EndTable();
        }
    }

    private void DrawMateria(PieceStatus status, bool local)
    {
        var check = status.Melds;
        var expected = status.Piece.Materia;
        var label = expected == null ? (local ? "Not specified" : "Refresh targets") : status.Match == null ? "Need gear" :
            check == null ? "Unavailable" : check.IsExact ? (expected.Count == 0 ? "No melds needed" : "Correct") :
            $"{check.Matched}/{expected.Count} correct" + (check.Unexpected.Count > 0 ? " + extra" : "");
        ImGui.TextColored(check is { IsExact: true } ? Green : Amber, label);
        if (!ImGui.IsItemHovered()) return;
        ImGui.BeginTooltip();
        ImGui.TextUnformatted("TARGET MATERIA");
        if (expected == null) ImGui.TextUnformatted(local ? "No materia target is specified for this piece." : "Refresh this set from its link to import meld targets.");
        else DrawMateriaList(expected, "None (target expects no materia)");
        if (status.Match != null)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"ON MATCHED COPY ({status.Match.Location})");
            if (status.Match.Materia == null) ImGui.TextUnformatted("Materia data unavailable.");
            else DrawMateriaList(status.Match.Materia, "None");
        }
        if (check is { IsExact: false })
        {
            ImGui.Separator();
            ImGui.TextColored(Amber, "MISSING TARGET MELDS");
            DrawMateriaList(check.Missing, "None");
            ImGui.TextColored(Amber, "EXTRA / DIFFERENT MELDS");
            DrawMateriaList(check.Unexpected, "None");
        }
        ImGui.EndTooltip();
    }

    private void DrawMateriaList(IReadOnlyList<uint> ids, string emptyText)
    {
        if (ids.Count == 0) ImGui.TextUnformatted(emptyText);
        foreach (var group in ids.GroupBy(id => id))
            ImGui.TextUnformatted($"{group.Count()} x {plugin.ItemName(group.Key)}");
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
