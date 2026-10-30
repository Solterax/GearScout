using System.Diagnostics;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace GearScout;

internal sealed record ZoneChoice(uint TerritoryId, string Name);

internal sealed class ZoneCatalog(IDataManager data) : IDisposable
{
    private IEnumerator<TerritoryType>? rows;
    private readonly Dictionary<uint, string> names = [];
    private readonly List<ZoneChoice> items = [];
    private bool requested;
    public bool Ready { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyList<ZoneChoice> Items => items;
    public void Request() => requested = true;
    public string Name(uint id)
    {
        if (names.TryGetValue(id, out var name)) return name;
        name = data.GetExcelSheet<TerritoryType>().TryGetRow(id, out var row) ? Label(row) : "";
        return names[id] = string.IsNullOrWhiteSpace(name) ? $"Zone {id}" : name;
    }
    private static string Label(TerritoryType row)
    {
        var duty = row.ContentFinderCondition.IsValid ? row.ContentFinderCondition.Value.Name.ToString() : "";
        return !string.IsNullOrWhiteSpace(duty) ? duty : row.PlaceName.IsValid ? row.PlaceName.Value.Name.ToString() : "";
    }
    public void Update()
    {
        if (!requested || Ready || Error != null) return;
        try
        {
            rows ??= data.GetExcelSheet<TerritoryType>().GetEnumerator();
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed.TotalMilliseconds < 0.5)
            {
                if (!rows.MoveNext()) { Ready = true; rows.Dispose(); rows = null; return; }
                var row = rows.Current;
                if (row.RowId == 0 || row.RowId > ushort.MaxValue) continue;
                var name = Label(row);
                if (string.IsNullOrWhiteSpace(name)) continue;
                names[row.RowId] = name;
                items.Add(new(row.RowId, name));
            }
        }
        catch (Exception ex) { Error = ex.Message; Plugin.Log.Warning(ex, "Could not load duty/zone search"); }
    }
    public void Dispose() => rows?.Dispose();
}
