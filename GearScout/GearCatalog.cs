using System.Diagnostics;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace GearScout;

internal sealed class GearCatalog(IDataManager data) : IDisposable
{
    private IEnumerator<Item>? rows;
    private readonly List<GearCandidate> items = [];
    private readonly Dictionary<uint, ulong> jobs = [];
    public bool Ready { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyList<GearCandidate> Items => items;

    // Build from local game sheets in small framework-thread batches, never on every keystroke.
    public void Update()
    {
        if (Ready || Error != null) return;
        try
        {
            rows ??= data.GetExcelSheet<Item>().GetEnumerator();
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 2)
            {
                if (!rows.MoveNext()) { Ready = true; rows.Dispose(); rows = null; return; }
                var row = rows.Current;
                if (row.EquipSlotCategory.RowId == 0 || !row.EquipSlotCategory.IsValid || !row.ClassJobCategory.IsValid) continue;
                var category = row.EquipSlotCategory.Value;
                ulong slots = 0;
                void Slot(string slot, bool allowed) { if (allowed) slots |= GearSearch.SlotBit(slot); }
                Slot("Weapon", category.MainHand > 0); Slot("OffHand", category.OffHand > 0);
                Slot("Head", category.Head > 0); Slot("Body", category.Body > 0); Slot("Hand", category.Gloves > 0);
                Slot("Legs", category.Legs > 0); Slot("Feet", category.Feet > 0); Slot("Ears", category.Ears > 0);
                Slot("Neck", category.Neck > 0); Slot("Wrist", category.Wrists > 0);
                Slot("RingLeft", category.FingerL > 0 || category.FingerR > 0);
                Slot("RingRight", category.FingerL > 0 || category.FingerR > 0);
                if (slots == 0) continue;
                if (!jobs.TryGetValue(row.ClassJobCategory.RowId, out var mask))
                    jobs[row.ClassJobCategory.RowId] = mask = JobMask(row.ClassJobCategory.Value);
                var name = row.Name.ToString();
                if (mask != 0 && !string.IsNullOrWhiteSpace(name))
                    items.Add(new(row.RowId, name, row.LevelItem.RowId, row.LevelEquip, slots, mask, row.IsUnique));
            }
        }
        catch (Exception ex) { Error = ex.Message; Plugin.Log.Error(ex, "Could not build gear search index"); }
    }

    private static ulong JobMask(ClassJobCategory c)
    {
        (string Job, bool Allowed)[] flags =
        [
            ("GLA", c.GLA), ("PGL", c.PGL), ("MRD", c.MRD), ("LNC", c.LNC), ("ARC", c.ARC), ("CNJ", c.CNJ), ("THM", c.THM),
            ("ACN", c.ACN), ("ROG", c.ROG), ("PLD", c.PLD), ("MNK", c.MNK), ("WAR", c.WAR), ("DRG", c.DRG), ("BRD", c.BRD),
            ("WHM", c.WHM), ("BLM", c.BLM), ("SMN", c.SMN), ("SCH", c.SCH), ("NIN", c.NIN), ("MCH", c.MCH), ("DRK", c.DRK),
            ("AST", c.AST), ("SAM", c.SAM), ("RDM", c.RDM), ("BLU", c.BLU), ("GNB", c.GNB), ("DNC", c.DNC), ("RPR", c.RPR),
            ("SGE", c.SGE), ("VPR", c.VPR), ("PCT", c.PCT), ("CRP", c.CRP), ("BSM", c.BSM), ("ARM", c.ARM), ("GSM", c.GSM),
            ("LTW", c.LTW), ("WVR", c.WVR), ("ALC", c.ALC), ("CUL", c.CUL), ("MIN", c.MIN), ("BTN", c.BTN), ("FSH", c.FSH),
        ];
        ulong mask = 0;
        foreach (var (job, allowed) in flags) if (allowed) mask |= GearSearch.JobBit(job);
        return mask;
    }

    public void Dispose() => rows?.Dispose();
}
