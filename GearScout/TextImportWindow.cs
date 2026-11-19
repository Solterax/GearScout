using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace GearScout;

internal sealed class TextImportWindow(Plugin plugin) : Window("Import pasted gear###GearScoutTextImport", ImGuiWindowFlags.NoResize)
{
    private string text = "";
    private string name = "Imported gear set";
    private string job = "";
    private string error = "";
    private TextGearImport? preview;

    public void Open()
    {
        plugin.Catalog.Request(); IsOpen = true; error = "";
        Size = new(820, 760); SizeCondition = ImGuiCond.Always;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new(820, 760), MaximumSize = new(820, 760) };
    }

    public override void Draw()
    {
        ImGui.TextWrapped("Paste item names with materia listed beneath each piece. Plain text, Markdown bold names, HQ suffixes and repeated rings are supported. Names must match the language of your game data.");
        if (ImGui.Button("Paste clipboard")) { text = ImGui.GetClipboardText(); preview = null; error = ""; }
        ImGui.SameLine();
        if (ImGui.Button("Clear")) { text = ""; preview = null; error = ""; }
        if (ImGui.InputTextMultiline("##gearText", ref text, 128_001, new Vector2(-1, 220))) { preview = null; error = ""; }
        ImGui.BeginDisabled(!plugin.Catalog.Ready || string.IsNullOrWhiteSpace(text));
        if (ImGui.Button("Review import"))
        {
            try
            {
                plugin.MeldingData.Load();
                if (plugin.MeldingData.Error != null) throw new InvalidOperationException(plugin.MeldingData.Error);
                preview = TextGearImporter.Parse(text, plugin.Catalog.Items, plugin.MeldingData.Materia.Values);
                job = preview.Jobs.Contains(plugin.CurrentJob) ? plugin.CurrentJob : preview.Jobs[0];
                error = "";
            }
            catch (Exception ex) { preview = null; error = ex.Message; }
        }
        ImGui.EndDisabled();
        if (plugin.Catalog.Error != null) ImGui.TextWrapped("Gear data unavailable: " + plugin.Catalog.Error);
        else if (!plugin.Catalog.Ready) ImGui.TextUnformatted("Loading local gear names...");
        if (!string.IsNullOrEmpty(error)) ImGui.TextColored(new Vector4(1, 0.65f, 0.35f, 1), error);
        if (preview == null) return;
        ImGui.Separator();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##importName", "Set name", ref name, 120);
        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo("Job", job))
        {
            foreach (var choice in preview.Jobs) if (ImGui.Selectable(choice, choice == job)) job = choice;
            ImGui.EndCombo();
        }
        ImGui.TextUnformatted($"{preview.Pieces.Count} pieces / {preview.Pieces.Sum(p => p.Materia?.Count ?? 0)} materia");
        if (preview.HasHqLabels) ImGui.TextWrapped("HQ labels were recognized. Gear sets track item and materia targets; HQ/NQ quality is not tracked.");
        if (ImGui.BeginTable("textPreview", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 230)))
        {
            ImGui.TableSetupColumn("Slot"); ImGui.TableSetupColumn("Gear"); ImGui.TableSetupColumn("Materia");
            ImGui.TableSetupScrollFreeze(0, 1); ImGui.TableHeadersRow();
            foreach (var piece in preview.Pieces)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextUnformatted(GearSearch.SlotName(piece.Slot));
                ImGui.TableNextColumn(); ImGui.TextWrapped(plugin.ItemName(piece.ItemId));
                ImGui.TableNextColumn();
                if (piece.Materia!.Count == 0) ImGui.TextUnformatted("None");
                foreach (var id in piece.Materia) ImGui.TextWrapped(plugin.ItemName(id));
            }
            ImGui.EndTable();
        }
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(name));
        if (ImGui.Button("Save imported set"))
        {
            try { plugin.SaveTextImport(preview.CreateSet(name, job)); IsOpen = false; }
            catch (Exception ex) { error = ex.Message; }
        }
        ImGui.EndDisabled(); ImGui.SameLine();
        if (ImGui.Button("Cancel")) IsOpen = false;
    }
}
