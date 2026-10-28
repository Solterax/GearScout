using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using Bounds = FFXIVClientStructs.FFXIV.Common.Math.Bounds;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace GearScout;

internal sealed unsafe class LootHighlighter(Plugin plugin) : IDisposable
{
    private bool faulted;
    private bool nameFaulted;
    private nint coloredAddon;
    private readonly Dictionary<nint, NameStyle> originalTextColors = [];
    private static readonly ByteColor NameBlue = new() { R = 88, G = 174, B = 255, A = 255 };
    private sealed record NameStyle(uint Color, byte[] OriginalText, byte[] AppliedText);

    public void Initialize()
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, "NeedGreed", OnNativeDraw);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "NeedGreed", OnFinalize);
    }

    // Icon overlays retain no native pointers. Native name colors are restored through live-node traversal.
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
                DrawWantedBadge(draw, max.X, min.Y, scale);
            }
        }
        finally { draw.PopClipRect(); }
    }

    private static void DrawWantedBadge(ImDrawListPtr draw, float right, float top, float scale)
    {
        scale *= 1.5f;
        var center = new Vector2(right - 8 * scale, top + 8 * scale);
        var blue = ImGui.ColorConvertFloat4ToU32(new Vector4(0.35f, 0.72f, 1f, 1));
        draw.AddCircleFilled(center, 7 * scale, ImGui.ColorConvertFloat4ToU32(new Vector4(0.025f, 0.07f, 0.14f, 0.96f)));
        draw.AddCircle(center, 7 * scale, blue, 0, 1.25f * scale);
        // Draw punctuation as geometry so the badge scales with the native icon, not the plugin font.
        draw.AddLine(center + new Vector2(0, -4 * scale), center + new Vector2(0, 1 * scale), blue, 2.2f * scale);
        draw.AddCircleFilled(center + new Vector2(0, 4 * scale), 1.2f * scale, blue);
    }

    private void OnNativeDraw(AddonEvent type, AddonArgs args)
    {
        var addon = args.GetAddonPointer<AddonNeedGreed>();
        if (addon == null) return;
        try
        {
            RestoreNames(addon);
            if (nameFaulted || !plugin.Config.HighlightLoot || string.IsNullOrEmpty(plugin.CurrentJob) ||
                Plugin.GameGui.GameUiHidden || !addon->IsVisible) return;
            var loot = Loot.Instance();
            if (loot == null) return;
            var targets = LootMatcher.MissingItems(plugin.Config.Sets, plugin.CurrentJob,
                plugin.Config.LootCurrentJobOnly, plugin.Owned);
            if (targets.Count == 0) return;
            var rolls = new List<LootRollEntry>(loot->Items.Length);
            foreach (ref readonly var entry in loot->Items)
                rolls.Add(new(entry.ItemId, (entry.RollState is RollState.UpToNeed or RollState.UpToGreed) &&
                    entry.RollResult == RollResult.UnAwarded && entry.Time > 0));
            coloredAddon = (nint)addon;
            var node = addon->GetNodeById(6);
            var list = node == null ? null : node->GetAsAtkComponentList();
            var count = list == null ? 0 : Math.Clamp(list->GetItemCount(), 0, addon->Items.Length);
            var rowOwners = new HashSet<nint>();
            for (var index = 0; index < count; index++)
            {
                var renderer = list->GetItemRenderer(index);
                if (renderer != null && renderer->OwnerNode != null) rowOwners.Add((nint)renderer->OwnerNode);
            }
            var selected = addon->SelectedItemIndex;
            if (selected >= 0 && selected < addon->Items.Length && addon->Items[selected].ItemId != 0)
            {
                if (LootMatcher.CanHighlight(addon->Items[selected].ItemId, selected, rolls, targets))
                    TintNames(&addon->UldManager, plugin.ItemName(LootMatcher.BaseItemId(addon->Items[selected].ItemId)), rowOwners);
            }
            else
            {
                // Some layouts show a preview name before assigning SelectedItemIndex.
                for (var index = 0; index < addon->Items.Length; index++)
                    if (LootMatcher.CanHighlight(addon->Items[index].ItemId, index, rolls, targets))
                        TintNames(&addon->UldManager, plugin.ItemName(LootMatcher.BaseItemId(addon->Items[index].ItemId)), rowOwners);
            }
            for (var index = 0; index < count; index++)
            {
                if (!LootMatcher.CanHighlight(addon->Items[index].ItemId, index, rolls, targets)) continue;
                var renderer = list->GetItemRenderer(index);
                if (renderer == null || renderer->ListItemIndex != index) continue;
                TintNames(&renderer->UldManager, plugin.ItemName(LootMatcher.BaseItemId(addon->Items[index].ItemId)));
            }
        }
        catch (Exception ex)
        {
            nameFaulted = true;
            Plugin.Log.Warning(ex, "Loot name coloring disabled until GearScout is reloaded");
            RestoreNames(addon);
        }
    }

    private void TintNames(AtkUldManager* manager, string expectedName, HashSet<nint>? excludedParents = null)
    {
        var nodes = new List<nint>();
        CollectTextNodes(manager, nodes, [], true);
        foreach (var address in nodes)
        {
            var text = (AtkTextNode*)address;
            if (!Visible((AtkResNode*)text) || InsideExcludedParent((AtkResNode*)text, excludedParents)) continue;
            var original = text->NodeText.AsSpan().ToArray();
            var applied = LootNameFormatting.ForBlueName(original, expectedName);
            if (applied == null) continue;
            originalTextColors.TryAdd(address, new(text->TextColor.RGBA, original, applied));
            text->TextColor = NameBlue;
            text->SetText(applied.Append((byte)0).ToArray()); // Also invalidates the native text layout cache.
            text->IsDirty = true;
        }
    }

    private static bool InsideExcludedParent(AtkResNode* node, HashSet<nint>? excluded)
    {
        if (excluded == null || excluded.Count == 0) return false;
        for (var depth = 0; node != null && depth < 64; node = node->ParentNode, depth++)
            if (excluded.Contains((nint)node)) return true;
        return false;
    }

    private void RestoreNames(AddonNeedGreed* addon)
    {
        if (originalTextColors.Count == 0) return;
        if ((nint)addon == coloredAddon)
        {
            var nodes = new List<nint>();
            CollectTextNodes(&addon->UldManager, nodes, [], true);
            foreach (var address in nodes)
            {
                var text = (AtkTextNode*)address;
                // Only dereference currently live nodes, and only restore colors still owned by GearScout.
                if (!originalTextColors.TryGetValue(address, out var original)) continue;
                if (text->NodeText.AsSpan().SequenceEqual(original.AppliedText))
                    text->SetText(original.OriginalText.Append((byte)0).ToArray());
                if (text->TextColor.RGBA == NameBlue.RGBA) text->TextColor.RGBA = original.Color;
                text->IsDirty = true;
            }
        }
        originalTextColors.Clear();
        coloredAddon = 0;
    }

    private static void CollectTextNodes(AtkUldManager* manager, List<nint> texts, HashSet<nint> visited, bool recurse, int depth = 0)
    {
        if (manager == null || manager->NodeList == null || manager->NodeListCount > 4096 || depth > 8) return;
        for (var index = 0; index < manager->NodeListCount && visited.Count < 4096; index++)
        {
            var node = manager->NodeList[index];
            if (node == null || !visited.Add((nint)node)) continue;
            if (node->Type == NodeType.Text) texts.Add((nint)node);
            else if (recurse && (int)node->Type >= 1000)
            {
                var component = node->GetComponent();
                if (component != null) CollectTextNodes(&component->UldManager, texts, visited, true, depth + 1);
            }
        }
    }

    private void OnFinalize(AddonEvent type, AddonArgs args) => RestoreNames(args.GetAddonPointer<AddonNeedGreed>());

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, "NeedGreed", OnNativeDraw);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "NeedGreed", OnFinalize);
        var addon = Plugin.GameGui.GetAddonByName<AddonNeedGreed>("NeedGreed");
        if (addon != null) RestoreNames(addon);
        originalTextColors.Clear();
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
