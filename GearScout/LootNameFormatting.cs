using Dalamud.Game.Text.SeStringHandling;

namespace GearScout;

internal static class LootNameFormatting
{
    public static byte[]? ForBlueName(byte[] nativeText, string expectedName)
    {
        var parsed = SeString.Parse(nativeText);
        if (!string.Equals(parsed.TextValue.Trim(), expectedName.Trim(), StringComparison.Ordinal)) return null;
        // Native rarity foreground/glow macros override the text node's base color.
        // Keep non-color payloads such as icons while GearScout supplies the blue base color.
        return new SeString(parsed.Payloads.Where(p => p.Type is not PayloadType.UIForeground and not PayloadType.UIGlow).ToList()).Encode();
    }
}
