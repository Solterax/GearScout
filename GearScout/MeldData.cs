using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Lumina.Excel;

namespace GearScout;

internal sealed class MeldData(IDataManager? data, Lumina.GameData? gameData = null)
{
    private ExcelSheet<T> Sheet<T>() where T : struct, IExcelRow<T> =>
        (data != null ? data.GetExcelSheet<T>() : gameData!.GetExcelSheet<T>()) ?? throw new InvalidOperationException($"Missing {typeof(T).Name} sheet.");
    public Dictionary<uint, MateriaOption> Materia { get; } = [];
    public string? Error { get; private set; }
    private bool loaded;
    private readonly Dictionary<(uint, bool), MeldGearRules?> rules = [];

    public void Load()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            foreach (var row in Sheet<Lumina.Excel.Sheets.Materia>())
            for (var grade = 0; grade < row.Item.Count; grade++)
            {
                if (!row.Item[grade].IsValid || row.Item[grade].RowId == 0 || grade >= row.Value.Count) continue;
                if (!Sheet<MateriaGrade>().TryGetRow((uint)grade, out var rates)) continue;
                var item = row.Item[grade].Value;
                Materia[item.RowId] = new(item.RowId, item.Name.ToString(), row.BaseParam.RowId, row.Value[grade], item.LevelItem.RowId,
                    rates.OvermeldNQPercent.Select(p => p / 100.0).ToArray(), rates.OvermeldHQPercent.Select(p => p / 100.0).ToArray(), rates.ReturnRate / 100.0);
            }
        }
        catch (Exception ex) { Error = ex.Message; Plugin.Log.Error(ex, "Could not read materia planning data"); }
    }

    public MeldGearRules? Rules(uint id, bool hq)
    {
        Load();
        if (rules.TryGetValue((id, hq), out var cached)) return cached;
        if (!Sheet<Item>().TryGetRow(id, out var item) || !item.LevelItem.IsValid) return null;
        var stats = new Dictionary<uint, int>();
        for (var i = 0; i < item.BaseParam.Count; i++)
            if (item.BaseParam[i].RowId != 0) stats[item.BaseParam[i].RowId] = stats.GetValueOrDefault(item.BaseParam[i].RowId) + item.BaseParamValue[i];
        if (hq)
            for (var i = 0; i < item.BaseParamSpecial.Count; i++)
                if (item.BaseParamSpecial[i].RowId != 0) stats[item.BaseParamSpecial[i].RowId] = stats.GetValueOrDefault(item.BaseParamSpecial[i].RowId) + item.BaseParamValueSpecial[i];
        var caps = new Dictionary<uint, int>();
        var names = new Dictionary<uint, string>();
        foreach (var idParam in Materia.Values.Select(m => m.Parameter).Distinct())
        {
            if (!Sheet<BaseParam>().TryGetRow(idParam, out var param) || item.BaseParamModifier >= param.MeldParam.Count) continue;
            names[idParam] = param.Name.ToString();
            var level = item.LevelItem.Value;
            int baseValue = idParam switch
            {
                6 => level.Piety, 10 => level.GP, 11 => level.CP, 19 => level.Tenacity, 22 => level.DirectHitRate,
                27 => level.CriticalHit, 44 => level.Determination, 45 => level.SkillSpeed, 46 => level.SpellSpeed,
                70 => level.Craftsmanship, 71 => level.Control, 72 => level.Gathering, 73 => level.Perception, _ => 0,
            };
            int percent = item.EquipSlotCategory.RowId switch
            {
                1 => param.OneHandWeaponPercent, 2 => param.OffHandPercent, 3 => param.HeadPercent, 4 => param.ChestPercent,
                5 => param.HandsPercent, 6 => param.WaistPercent, 7 => param.LegsPercent, 8 => param.FeetPercent,
                9 => param.EarringPercent, 10 => param.NecklacePercent, 11 => param.BraceletPercent, 12 => param.RingPercent,
                13 or 14 => param.TwoHandWeaponPercent, 15 => param.ChestHeadPercent, 16 => param.ChestHeadLegsFeetPercent,
                18 => param.LegsFeetPercent, 19 => param.HeadChestHandsLegsFeetPercent, 20 => param.ChestLegsGlovesPercent,
                21 => param.ChestLegsFeetPercent, _ => 0,
            };
            var modifier = param.MeldParam[item.BaseParamModifier];
            if (baseValue > 0 && percent > 0 && modifier > 0)
                caps[idParam] = (int)Math.Floor(baseValue * percent / (modifier * 10.0) + 0.5);
        }
        return rules[(id, hq)] = new(item.MateriaSlotCount, item.IsAdvancedMeldingPermitted, item.LevelItem.RowId, stats, caps, names);
    }
}
