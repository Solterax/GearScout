using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace GearScout;

internal sealed class MateriaTargetEditor(Plugin plugin)
{
    private GearPiece? piece;
    private List<uint> target = [];
    private string query = "";
    public bool Changed { get; private set; }
    public void Open(GearPiece selected)
    {
        piece = selected; target = selected.Materia?.ToList() ?? []; query = "";
        ImGui.OpenPopup("Materia targets");
    }

    public void Draw()
    {
        Changed = false;
        ImGui.SetNextWindowSize(new Vector2(680, 530), ImGuiCond.Always);
        if (!ImGui.BeginPopup("Materia targets", ImGuiWindowFlags.NoResize)) return;
        plugin.MeldingData.Load();
        ImGui.TextUnformatted(piece == null ? "Materia targets" : plugin.ItemName(piece.ItemId));
        ImGui.TextWrapped("Choose up to five materia, including repeats. The planner chooses a legal order while preserving usable existing melds.");
        for (var index = 0; index < target.Count; index++)
        {
            ImGui.PushID(index);
            if (ImGui.SmallButton("Remove")) { target.RemoveAt(index); ImGui.PopID(); break; }
            ImGui.SameLine(); ImGui.TextUnformatted($"{index + 1}. {plugin.ItemName(target[index])}");
            ImGui.PopID();
        }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##materiaQuery", "Search materia by name or item ID", ref query, 150);
        if (plugin.MeldingData.Error != null) ImGui.TextWrapped(plugin.MeldingData.Error);
        if (ImGui.BeginChild("materiaResults", new Vector2(0, 220), true))
        {
            ImGui.BeginDisabled(target.Count >= 5);
            foreach (var m in plugin.MeldingData.Materia.Values.Where(m => m.Value > 0 &&
                query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(t => m.Name.Contains(t, StringComparison.OrdinalIgnoreCase) || m.ItemId.ToString() == t))
                .OrderByDescending(m => m.MinimumItemLevel).ThenBy(m => m.Name))
                if (ImGui.Selectable($"{m.Name}##materia-{m.ItemId}")) target.Add(m.ItemId);
            ImGui.EndDisabled();
        }
        ImGui.EndChild();
        if (ImGui.Button("Apply targets") && piece != null)
        {
            piece.Materia = target.ToList(); Changed = true; ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("No melds wanted") && piece != null) { piece.Materia = []; Changed = true; ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }
}
