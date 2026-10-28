using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace GearScout;

internal sealed class SetEditorWindow(Plugin plugin) : Window("Build gear set###GearScoutEditor")
{
    private TrackedSet? draft;
    private string name = "";
    private string slot = "";
    private string query = "";
    private string error = "";
    private bool openPicker;
    private bool focusSearch;
    private bool importedCopy;
    private List<GearCandidate> results = [];
    private string? lastSearch;

    public void Create(string job)
    {
        Open(new TrackedSet { Job = job, Name = $"New {job} set" });
    }

    public void CreateGlam() => Open(new TrackedSet { IsGlam = true, Name = "New glam set" });

    public void Open(TrackedSet source, string? initialSlot = null)
    {
        draft = GearSearch.Draft(source);
        name = draft.Name;
        error = "";
        importedCopy = !string.IsNullOrWhiteSpace(source.SourceUrl);
        slot = initialSlot ?? "";
        query = "";
        lastSearch = null;
        openPicker = initialSlot != null;
        IsOpen = true;
        Size = new Vector2(760, 630);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(560, 420), MaximumSize = new Vector2(float.MaxValue) };
    }

    public override void Draw()
    {
        if (draft == null) return;
        ImGui.TextUnformatted(draft.IsGlam ? "Glam set — all jobs" : $"Gear set for {draft.Job}");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##name", "Set name", ref name, 120);
        if (importedCopy) ImGui.TextWrapped("This will save a local copy. The imported set stays unchanged.");
        ImGui.TextWrapped(draft.IsGlam
            ? "Click a slot to search gear from every job. Glam sets stay visible on all jobs and track appearances."
            : "Click a gear piece or an empty slot to search. Results fit this slot and job, including gear above your current level.");
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(name));
        if (ImGui.Button("Save set"))
        {
            draft.Name = name.Trim();
            try { plugin.SaveLocalSet(draft); IsOpen = false; }
            catch (Exception ex) { error = "Could not save set: " + ex.Message; }
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) IsOpen = false;
        if (!string.IsNullOrEmpty(error)) ImGui.TextWrapped(error);
        ImGui.Separator();

        if (ImGui.BeginTable("editorSlots", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(0, Math.Max(100, ImGui.GetContentRegionAvail().Y))))
        {
            ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Gear piece — click to search", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##clear", ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableHeadersRow();
            foreach (var key in GearSearch.Slots)
            {
                var piece = draft.Pieces.FirstOrDefault(p => p.Slot == key);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(GearSearch.SlotName(key));
                ImGui.TableNextColumn();
                if (ImGui.Selectable($"{(piece == null ? "Select gear..." : plugin.ItemName(piece.ItemId))}##{key}"))
                {
                    slot = key; query = ""; lastSearch = null; openPicker = true;
                }
                ImGui.TableNextColumn();
                if (piece != null && ImGui.SmallButton($"Clear##{key}")) draft.Pieces.RemoveAll(p => p.Slot == key);
            }
            ImGui.EndTable();
        }
        if (openPicker)
        {
            ImGui.OpenPopup("Find gear"); openPicker = false; focusSearch = true;
        }
        DrawPicker();
    }

    private void DrawPicker()
    {
        ImGui.SetNextWindowSize(new Vector2(680, 440), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup("Find gear")) return;
        ImGui.TextUnformatted($"{(draft!.IsGlam ? "Glam — all jobs" : draft.Job)} / {GearSearch.SlotName(slot)}");
        if (focusSearch) { ImGui.SetKeyboardFocusHere(); focusSearch = false; }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##query", "Search gear by name or item ID...", ref query, 200);
        if (plugin.Catalog.Error != null) ImGui.TextWrapped("Gear search unavailable: " + plugin.Catalog.Error);
        else if (!plugin.Catalog.Ready) ImGui.TextUnformatted("Loading local gear data...");
        else
        {
            var key = draft.IsGlam + "|" + draft.Job + "|" + slot + "|" + query;
            if (lastSearch != key)
            {
                results = GearSearch.Search(plugin.Catalog.Items, draft.Job, slot, query, draft.IsGlam);
                lastSearch = key;
            }
            ImGui.TextUnformatted($"{results.Count} matching items. Highest item level first.");
            if (ImGui.BeginTable("searchResults", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 305)))
            {
                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Item level", ImGuiTableColumnFlags.WidthFixed, 75);
                ImGui.TableSetupColumn("Level", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableHeadersRow();
                // Clip long lists instead of drawing thousands of matching accessories each frame.
                var clipper = ImGui.ImGuiListClipper();
                try
                {
                    clipper.Begin(results.Count);
                    while (clipper.Step())
                    for (var index = clipper.DisplayStart; index < clipper.DisplayEnd; index++)
                    {
                        var item = results[index];
                        var blocked = GearSearch.ConflictsWithOtherRing(draft, slot, item);
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.BeginDisabled(blocked);
                        if (ImGui.Selectable($"{item.Name}##{item.ItemId}"))
                        {
                            GearSearch.SetPiece(draft, slot, item);
                            ImGui.CloseCurrentPopup();
                        }
                        ImGui.EndDisabled();
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                            ImGui.SetTooltip(blocked ? "This unique ring is already in the other ring slot." : $"Item ID: {item.ItemId}");
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(item.ItemLevel.ToString());
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(item.EquipLevel.ToString());
                    }
                }
                finally { clipper.Destroy(); }
                ImGui.EndTable();
            }
            if (results.Count == 0) ImGui.TextWrapped(draft.IsGlam ? "No matching gear. Try a shorter search." : "No matching gear. Try a shorter search; some jobs have no off-hand equipment.");
        }
        if (ImGui.Button("Close search")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }
}
