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

// Exact Addon row 2378 bytes from installed English game data, including its numeric macro.
var inventoryTemplate = Convert.FromHexString("456E7472757374206F72207769746864726177206974656D732E2028536C6F74732066696C6C65643A20022206E802FF022C0329");
var quitTemplate = new SeString(new TextPayload("Quit.")).Encode();
string[] menu = ["Entrust or withdraw gil. (Gil entrusted: 0)", "Entrust or withdraw items. (Slots filled: 0)", "Quit."];
void Match(byte[] template, string[] entries, int expected)
{
    var found = RetainerMenuMatching.FindEntry(template, entries);
    if (found != expected) throw new Exception($"Retainer menu match expected {expected}, got {found}");
}
Match(inventoryTemplate, menu, 1);
Match(inventoryTemplate, ["Quit.", "Entrust or withdraw items. (Slots filled: 175)"], 1);
Match(quitTemplate, menu, 2);
Match(inventoryTemplate, [menu[0], "Sell items in your inventory on the market."], -1);
Match(inventoryTemplate, [menu[1], menu[1]], -1);
Match(quitTemplate, ["Quit. Another action"], -1);
var numericMacro = SeString.Parse(inventoryTemplate).Payloads.First(p => p is not TextPayload);
var localized = new SeString(new TextPayload("Objekte verwalten. (Belegt: "), numericMacro, new TextPayload(")")).Encode();
Match(localized, ["Gil verwalten.", "Objekte verwalten. (Belegt: 42)"], 1);
Match(new SeString(numericMacro).Encode(), menu, -1);
Console.WriteLine("8 retainer menu checks passed: screenshot label, changing counter, Quit, unrelated entries, ambiguity, exact static text, localization and empty prefix.");
