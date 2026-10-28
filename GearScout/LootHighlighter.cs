using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using Bounds = FFXIVClientStructs.FFXIV.Common.Math.Bounds;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace GearScout;

internal sealed unsafe class LootHighlighter(Plugin plugin)
{
    private bool faulted;

    // Read native UI data afresh each draw. No node pointers survive the call and no game nodes are modified.
    public void Draw()
    {
        if (faulted || !plugin.Config.HighlightLoot || string.IsNullOrEmpty(plugin.CurrentJob) || Plugin.GameGui.GameUiHidden)
            return;
        try { DrawHighlights(); }
        catch (Exception ex)
        {
            faulted = true;
            Plugin.Log.Error(ex, "Loot highlighting disabled until GearScout is reloaded");
            plugin.ReportError("Loot highlighting encountered an error. Reload GearScout to retry; see /xllog for details.");
        }
    }

    private void DrawHighlights()
    {
        var addon = Plugin.GameGui.GetAddonByName<AddonNeedGreed>("NeedGreed");
        if (addon == null || !addon->IsVisible || !Visible(addon->RootNode)) return;
        var loot = Loot.Instance();
        if (loot == null) return;
        var targets = LootMatcher.MissingItems(plugin.Config.Sets, plugin.CurrentJob,
            plugin.Config.LootCurrentJobOnly, plugin.Owned);
        if (targets.Count == 0) return;

        // NeedGreed's item list is node 6. Validate its component type before casting.
        var listNode = addon->GetNodeById(6);
        if (!Visible(listNode)) return;
        var list = listNode->GetAsAtkComponentList();
        if (list == null) return;
        var count = list->GetItemCount();
        if (count < 0 || count > addon->Items.Length) return;
        var rolls = new List<LootRollEntry>(loot->Items.Length);
        foreach (ref readonly var entry in loot->Items)
            rolls.Add(new(entry.ItemId, (entry.RollState is RollState.UpToNeed or RollState.UpToGreed) &&
                entry.RollResult == RollResult.UnAwarded && entry.Time > 0));

        Bounds clip;
        listNode->GetBounds(&clip);
        if (clip.Width <= 0 || clip.Height <= 0) return;
        var origin = ImGui.GetMainViewport().Pos;
        var draw = ImGui.GetForegroundDrawList();
        draw.PushClipRect(origin + new Vector2(clip.Pos1.X, clip.Pos1.Y), origin + new Vector2(clip.Pos2.X, clip.Pos2.Y), true);
        try
        {
            for (var index = 0; index < count; index++)
            {
                var item = addon->Items[index];
                if (!LootMatcher.CanHighlight(item.ItemId, index, rolls, targets) || !list->IsItemVisible(index, false))
                    continue;
                var renderer = list->GetItemRenderer(index);
                if (renderer == null || renderer->ListItemIndex != index || !Visible((AtkResNode*)renderer->OwnerNode)) continue;
                var icon = IconNode(renderer);
                if (!Visible(icon)) continue;
                Bounds bounds;
                icon->GetBounds(&bounds);
                if (bounds.Width < 8 || bounds.Height < 8 || bounds.Width > 256 || bounds.Height > 256) continue;
                var min = origin + new Vector2(bounds.Pos1.X, bounds.Pos1.Y);
                var max = origin + new Vector2(bounds.Pos2.X, bounds.Pos2.Y);
                var scale = Math.Clamp(bounds.Width / 40f, 0.5f, 4f);
                // Several low-opacity outlines make a soft blue halo without obscuring the icon.
                for (var layer = 5; layer >= 1; layer--)
                {
                    var padding = new Vector2(layer * 0.85f * scale);
                    var alpha = (6 - layer) * 0.022f;
                    draw.AddRect(min - padding, max + padding,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.22f, 0.58f, 1f, alpha)),
                        4f * scale, ImDrawFlags.None, 2f * scale);
                }
                draw.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(0.32f, 0.67f, 1f, 0.55f)),
                    3f * scale, ImDrawFlags.None, 1.2f * scale);
            }
        }
        finally { draw.PopClipRect(); }
    }

    private static AtkResNode* IconNode(AtkComponentListItemRenderer* renderer)
    {
        var drag = renderer->DragDropComponent;
        if (drag != null && drag->AtkComponentIcon != null && drag->AtkComponentIcon->IconImage != null)
            return (AtkResNode*)drag->AtkComponentIcon->IconImage;
        for (var index = 0; index < renderer->UldManager.NodeListCount; index++)
        {
            var node = renderer->UldManager.NodeList[index];
            if (node == null || (int)node->Type < 1000) continue;
            var icon = node->GetAsAtkComponentIcon();
            if (icon != null && icon->IconImage != null) return (AtkResNode*)icon->IconImage;
            var childDrag = node->GetAsAtkComponentDragDrop();
            if (childDrag != null && childDrag->AtkComponentIcon != null && childDrag->AtkComponentIcon->IconImage != null)
                return (AtkResNode*)childDrag->AtkComponentIcon->IconImage;
        }
        // The standard NeedGreed renderer exposes its item image as node 12.
        var image = renderer->UldManager.SearchNodeById(12);
        return image != null && image->Type == NodeType.Image ? image : null;
    }

    private static bool Visible(AtkResNode* node)
    {
        if (node == null) return false;
        for (var depth = 0; node != null; node = node->ParentNode, depth++)
            if (depth > 64 || !node->IsVisible() || node->Color.A == 0) return false;
        return true;
    }
}
