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

if (args.Length == 2 && args[0] == "--game-data")
{
    using var game = new Lumina.GameData(args[1]);
    var meldData = new MeldData(null, game);
    meldData.Load();
    if (meldData.Error != null || meldData.Materia.Count == 0) throw new Exception("Materia game-data adapter failed");
    var xi = meldData.Materia.Values.First(m => m.Name == "Savage Aim Materia XI");
    var xii = meldData.Materia.Values.First(m => m.Name == "Savage Aim Materia XII");
    var accessory = meldData.Rules(49306, true)!;
    var bodyRow = game.GetExcelSheet<Lumina.Excel.Sheets.Item>()!.First(i => i.IsAdvancedMeldingPermitted && i.MateriaSlotCount == 2 && i.LevelItem.RowId >= 690 && i.EquipSlotCategory.RowId == 4);
    var body = meldData.Rules(bodyRow.RowId, true)!;
    if (accessory.GuaranteedSlots != 1 || !accessory.Advanced || MeldPlanner.Rate(accessory, xi, true, 4) != 0.05 ||
        MeldPlanner.Rate(body, xi, true, 4) != 0.07) throw new Exception("Actual accessory/armor slot rates are incorrect");
    if (MeldPlanner.Rate(accessory, xii, true, 2) != 0 || xii.RetrievalRate != 0.4 || xi.RetrievalRate != 0.8)
        throw new Exception("Current grade restriction/retrieval data is incorrect");
    if (accessory.StatCaps.GetValueOrDefault(xi.Parameter) <= 0 || accessory.StatCaps.GetValueOrDefault(xi.Parameter) < accessory.BaseStats.GetValueOrDefault(xi.Parameter))
        throw new Exception("Actual item stat-cap calculation failed");
    if (meldData.Rules(49638, false)!.Advanced) throw new Exception("Guaranteed-only accessory incorrectly permits overmelding");
    Console.WriteLine("5 installed-game melding checks passed: materia lookup, accessory/armor rates, grade/retrieval rules, stat caps and advanced-melding permission.");
}
