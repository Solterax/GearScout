using GearScout;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

var name = "Skallic Amulet of Fending";
var source = new SeString(new UIForegroundPayload(500), new UIGlowPayload(500), new TextPayload(name),
    UIForegroundPayload.UIForegroundOff, UIGlowPayload.UIGlowOff).Encode();
var original = (byte[])source.Clone();
var result = LootNameFormatting.ForBlueName(source, name) ?? throw new Exception("Name failed to match");
var parsed = SeString.Parse(result);
if (parsed.TextValue != name || parsed.Payloads.Any(p => p.Type is PayloadType.UIForeground or PayloadType.UIGlow))
    throw new Exception("Native color macros were retained");
if (!source.SequenceEqual(original)) throw new Exception("Original text was modified");
if (LootNameFormatting.ForBlueName(source, "Different item") != null) throw new Exception("Unrelated text would be recolored");
var plain = new SeString(new TextPayload(name)).Encode();
if (!LootNameFormatting.ForBlueName(plain, name)!.SequenceEqual(plain)) throw new Exception("Plain text changed");
Console.WriteLine("4 native SeString formatting checks passed: remove rarity colors; preserve original; reject unrelated names; preserve plain text.");
