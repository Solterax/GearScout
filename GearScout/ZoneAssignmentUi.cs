using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace GearScout;

internal sealed class ZoneAssignmentUi(Plugin plugin)
{
    private string query = "";
    private string? lastQuery;
    private List<ZoneChoice> results = [];

    public void Draw(TrackedSet set)
    {
        if (set.IsGlam || !ImGui.CollapsingHeader($"Auto-equip on entry ({set.AutoEquipTerritories.Count} assigned)###zoneAssignment")) return;
        var enabled = plugin.Config.AutoEquipOnEntry;
        if (ImGui.Checkbox("Enable auto-equip on zone entry (all sets)", ref enabled))
        {
            plugin.Config.AutoEquipOnEntry = enabled;
            try { plugin.Save(); }
            catch (Exception ex) { plugin.Config.AutoEquipOnEntry = !enabled; plugin.ReportError(ex.Message); }
        }
        ImGui.TextWrapped($"Assignments below apply to {set.Name} on {set.Job}. Gear is equipped after loading and cutscenes finish, while out of combat. Only items in bags and Armoury Chest can be equipped.");
        foreach (var id in set.AutoEquipTerritories.ToArray())
        {
            ImGui.PushID((int)id);
            if (ImGui.SmallButton("Remove")) plugin.RemoveZone(set, id);
            ImGui.SameLine(); ImGui.TextUnformatted($"{plugin.Zones.Name(id)} (#{id})");
            ImGui.PopID();
        }
        ImGui.BeginDisabled(set.Pieces.Count == 0);
        if (ImGui.Button("Add duty / zone..."))
        {
            plugin.Zones.Request(); query = ""; lastQuery = null;
            ImGui.OpenPopup("Assign duty or zone");
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(Plugin.Client.TerritoryType == 0);
        if (ImGui.Button("Assign current zone")) plugin.AssignZone(set, Plugin.Client.TerritoryType);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(plugin.Zones.Name(Plugin.Client.TerritoryType));
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        ImGui.TextWrapped("Assigning a zone replaces its previous assignment for this job. Other jobs keep their own assignments. Changes apply on your next entry.");
        if (!string.IsNullOrEmpty(plugin.ZoneEquipStatus)) ImGui.TextWrapped(plugin.ZoneEquipStatus);

        ImGui.SetNextWindowSize(new Vector2(620, 420), ImGuiCond.Always);
        if (!ImGui.BeginPopup("Assign duty or zone")) return;
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##zoneSearch", "Search duty or zone, e.g. Futures Rewritten", ref query, 200);
        if (plugin.Zones.Error != null) ImGui.TextWrapped("Zone search unavailable: " + plugin.Zones.Error);
        else if (!plugin.Zones.Ready) ImGui.TextUnformatted("Loading duty and zone names...");
        else
        {
            if (lastQuery != query)
            {
                var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                results = plugin.Zones.Items.Where(z => words.All(w => z.Name.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                    z.TerritoryId.ToString().Contains(w, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(z => z.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(z => z.TerritoryId).ToList();
                lastQuery = query;
            }
            ImGui.TextUnformatted($"{results.Count} duties / zones");
            if (results.Count > 100) ImGui.TextUnformatted("Showing the first 100 matches; type more to narrow the list.");
            if (ImGui.BeginChild("zoneResults", new Vector2(0, 280)))
            {
                foreach (var zone in results.Take(100))
                {
                    if (ImGui.Selectable($"{zone.Name} (#{zone.TerritoryId})", set.AutoEquipTerritories.Contains(zone.TerritoryId)))
                    { plugin.AssignZone(set, zone.TerritoryId); ImGui.CloseCurrentPopup(); }
                    if (ImGui.IsItemHovered())
                    {
                        var previous = plugin.Config.Sets.FirstOrDefault(s => !s.IsGlam && s.Id != set.Id &&
                            string.Equals(s.Job, set.Job, StringComparison.OrdinalIgnoreCase) && s.AutoEquipTerritories.Contains(zone.TerritoryId));
                        if (previous != null) ImGui.SetTooltip($"Replaces {previous.Name} for {set.Job} in this zone.");
                    }
                }
            }
            ImGui.EndChild();
        }
        if (ImGui.Button("Close")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }
}
