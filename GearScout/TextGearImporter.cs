using System.Net;
using System.Text.RegularExpressions;

namespace GearScout;

public sealed record TextGearImport(IReadOnlyList<GearPiece> Pieces, IReadOnlyList<string> Jobs, bool HasHqLabels)
{
    public TrackedSet CreateSet(string name, string job)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120) throw new ArgumentException("Enter a set name between 1 and 120 characters.");
        if (!Jobs.Contains(job, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Choose a job that can equip every imported piece.");
        return new() { Name = name.Trim(), Job = job.ToUpperInvariant(), Pieces = Pieces.Select(p => new GearPiece
            { Slot = p.Slot, ItemId = p.ItemId, Materia = p.Materia?.ToList() }).ToList() };
    }
}

public static class TextGearImporter
{
    public static TextGearImport Parse(string text, IEnumerable<GearCandidate> gear, IEnumerable<MateriaOption> materia)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 128_000) throw new ArgumentException("Paste a gear list up to 128,000 characters.");
        var gearNames = gear.ToLookup(g => Normalize(g.Name));
        var materiaNames = materia.Where(m => m.Value > 0).ToLookup(m => Normalize(m.Name));
        var pieces = new List<GearPiece>();
        var candidates = new List<GearCandidate>();
        GearPiece? current = null;
        ulong jobs = ulong.MaxValue;
        var hasHq = false;
        var lineNumber = 0;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            lineNumber++;
            var line = WebUtility.HtmlDecode(raw).Trim().TrimEnd('\\').Trim();
            line = Regex.Replace(line, @"\\([*_`\-])", "$1");
            if (line.Length == 0 || line == "\\" || line.StartsWith("```", StringComparison.Ordinal)) continue;
            var bullet = line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal) || line.StartsWith("• ", StringComparison.Ordinal);
            if (bullet) line = line[2..].Trim();
            line = line.Trim('*', '_', '`').Trim();
            var normalized = Normalize(line);
            var materiaMatches = materiaNames[normalized].DistinctBy(m => m.ItemId).ToList();
            if (bullet || materiaMatches.Count > 0)
            {
                if (current == null) throw Error("Materia appears before the first gear piece.");
                if (materiaMatches.Count != 1) throw Error($"Materia name is {(materiaMatches.Count == 0 ? "unknown" : "ambiguous")}: {line}");
                if (current.Materia!.Count >= 5) throw Error("A gear piece cannot have more than five materia.");
                current.Materia.Add(materiaMatches[0].ItemId);
                continue;
            }
            if (normalized.EndsWith(" HQ", StringComparison.Ordinal)) { hasHq = true; normalized = normalized[..^3].Trim(); }
            if (normalized.EndsWith('\uE03C')) { hasHq = true; normalized = normalized[..^1].Trim(); }
            var matches = gearNames[normalized].DistinctBy(g => g.ItemId).ToList();
            if (matches.Count != 1) throw Error($"Gear name is {(matches.Count == 0 ? "unknown" : "ambiguous")}: {line}");
            var item = matches[0];
            var slots = GearSearch.Slots.Where(s => (item.Slots & GearSearch.SlotBit(s)) != 0).ToList();
            if (slots.Count > 1 && slots.Any(s => s is not ("RingLeft" or "RingRight"))) throw Error($"Cannot determine one gear slot for {line}.");
            var slot = slots.FirstOrDefault(s => pieces.All(p => p.Slot != s));
            if (slot == null) throw Error($"No free gear slot for {line}. Only the two ring slots can repeat an item.");
            if (item.Unique && candidates.Any(c => c.ItemId == item.ItemId)) throw Error($"A unique item cannot be equipped twice: {line}");
            jobs &= item.Jobs;
            if (jobs == 0) throw Error("These pieces cannot all be equipped by the same job.");
            current = new() { Slot = slot, ItemId = item.ItemId, Materia = [] };
            pieces.Add(current); candidates.Add(item);
        }
        if (pieces.Count == 0) throw new ArgumentException("No gear pieces found in the pasted text.");
        return new(pieces, GearSearch.JobsForMask(jobs).ToList(), hasHq);

        ArgumentException Error(string message) => new($"Line {lineNumber}: {message}");
    }

    private static string Normalize(string value) => Regex.Replace(value.Replace('\u2019', '\'').Replace('\u2018', '\''), @"\s+", " ").Trim().ToUpperInvariant();
}
