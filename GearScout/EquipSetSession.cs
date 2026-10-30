namespace GearScout;

public enum EquipResultKind { Equipped, AlreadyEquipped, Missing, Stored, Failed, NotProcessed }
public sealed record EquipPieceResult(GearPiece Piece, EquipResultKind Kind, string Details);
public sealed record EquipRequest(GearPiece Piece, OwnedItem Source, int Destination);

public static class EquipSlots
{
    public static int Index(string slot) => slot switch
    {
        "Weapon" => 0, "OffHand" => 1, "Head" => 2, "Body" => 3, "Hand" => 4,
        "Legs" => 6, "Feet" => 7, "Ears" => 8, "Neck" => 9, "Wrist" => 10,
        "RingLeft" => 11, "RingRight" => 12, _ => -1,
    };
    public static bool SameItem(OwnedItem a, OwnedItem b) => a.ItemId == b.ItemId && a.HighQuality == b.HighQuality &&
        (b.Materia == null || a.Materia != null && a.Materia.SequenceEqual(b.Materia));
}

public sealed class EquipSetSession
{
    private List<GearPiece> pieces = [];
    private readonly Dictionary<int, OwnedItem> completed = [];
    private readonly Dictionary<(uint Id, string Location), int> storedUsed = [];
    private int index;
    private ulong owner;
    private string job = "";
    private long nextAction, waitingSince;
    public bool Running { get; private set; }
    public EquipRequest? Pending { get; private set; }
    public string SetName { get; private set; } = "";
    public List<EquipPieceResult> Results { get; } = [];
    public int Total => pieces.Count;
    public string Status { get; private set; } = "";

    public void Start(TrackedSet set, ulong characterId, string currentJob, long now)
    {
        if (Running) throw new InvalidOperationException("A set is already being equipped.");
        if (characterId == 0 || set.IsGlam || !string.Equals(set.Job, currentJob, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Select a normal set for your current job and finish loading first.");
        if (set.Pieces.Count == 0 || set.Pieces.Any(p => p.ItemId == 0 || EquipSlots.Index(p.Slot) < 0) ||
            set.Pieces.GroupBy(p => p.Slot).Any(g => g.Count() > 1)) throw new ArgumentException("This set has empty, invalid or duplicate target slots.");
        pieces = set.Pieces.OrderBy(p => EquipSlots.Index(p.Slot)).Select(p => new GearPiece { Slot = p.Slot, ItemId = p.ItemId, Materia = p.Materia?.ToList() }).ToList();
        owner = characterId; job = currentJob; SetName = set.Name; index = 0;
        completed.Clear(); storedUsed.Clear(); Results.Clear(); Pending = null;
        Running = true; nextAction = now; Status = $"Equipping {SetName}...";
    }

    public EquipRequest? Tick(ulong characterId, string currentJob, bool ready, bool blocked,
        IReadOnlyCollection<OwnedItem>? owned, long now)
    {
        if (!Running) return null;
        if (!ready || characterId != owner || !string.Equals(currentJob, job, StringComparison.OrdinalIgnoreCase) || owned == null)
        { Stop("Character, job, loading or inventory changed."); return null; }
        if (blocked) { Stop("The game is busy or combat started."); return null; }
        foreach (var worn in completed)
            if (!owned.Any(i => i.IsLive && i.Container == 1000 && i.InventorySlot == worn.Key && EquipSlots.SameItem(i, worn.Value)))
            {
                var old = Results.FindIndex(r => EquipSlots.Index(r.Piece.Slot) == worn.Key);
                if (old >= 0) Results[old] = Results[old] with { Kind = EquipResultKind.Failed, Details = "This slot changed after it was equipped. Review the current loadout." };
                Stop("Previously equipped gear changed during the equip pass."); return null;
            }
        if (Pending is { } pending)
        {
            var actual = owned.FirstOrDefault(i => i.IsLive && i.Container == 1000 && i.InventorySlot == pending.Destination);
            if (actual != null && EquipSlots.SameItem(actual, pending.Source))
            {
                completed[pending.Destination] = actual;
                Finish(EquipResultKind.Equipped, "Equipped and verified.", now);
            }
            else if (now - waitingSince >= 8000) Finish(EquipResultKind.Failed, "The game did not confirm the equip. Check equip requirements or inventory space.", now);
            return null;
        }
        if (now < nextAction) return null;
        var remaining = pieces.Skip(index).ToList();
        var live = owned.Where(i => i.IsLive && !(i.Container == 1000 && completed.ContainsKey(i.InventorySlot))).ToList();
        var assignments = GearMatcher.Match(remaining, live, false,
            (p, i) => (i.HighQuality == true ? 4 : 0) + (i.Container == 1000 && i.InventorySlot == EquipSlots.Index(p.Slot) ? 2 : 0));
        var target = pieces[index];
        var source = assignments[0].Match;
        var destination = EquipSlots.Index(target.Slot);
        if (source == null)
        {
            // Allocate saved copies across still-missing slots, not once per report row.
            var used = storedUsed.ToDictionary();
            var saved = new List<OwnedItem>();
            foreach (var item in owned.Where(i => !i.IsLive && i.Quantity > 0))
            {
                var key = (item.ItemId, item.Location); var consumed = Math.Min(item.Quantity, used.GetValueOrDefault(key));
                used[key] = used.GetValueOrDefault(key) - consumed;
                saved.Add(item with { Quantity = item.Quantity - consumed });
            }
            var remote = GearMatcher.Match(assignments.Where(s => s.Match == null).Select(s => s.Piece), saved).First(s => ReferenceEquals(s.Piece, target)).Match;
            if (remote != null)
            {
                var key = (remote.ItemId, remote.Location); storedUsed[key] = storedUsed.GetValueOrDefault(key) + 1;
                Finish(EquipResultKind.Stored, $"Found in {remote.Location}. Move it into bags or the Armoury Chest first.", now);
            }
            else Finish(EquipResultKind.Missing, "No separate copy found in inventory, Armoury Chest or saved storage.", now);
            return null;
        }
        if (source.Container == 1000 && source.InventorySlot == destination)
        {
            completed[destination] = source; Finish(EquipResultKind.AlreadyEquipped, "Already equipped.", now); return null;
        }
        Pending = new(target, source, destination); waitingSince = now;
        Status = $"{SetName}: equipping {GearSearch.SlotName(target.Slot)}...";
        return Pending;
    }

    public void Reject(string reason, long now)
    {
        if (Running && Pending != null) Finish(EquipResultKind.Failed, reason, now);
    }
    public void Stop(string reason = "Stopped by you.")
    {
        if (!Running) return;
        while (index < pieces.Count) Results.Add(new(pieces[index++], EquipResultKind.NotProcessed, reason));
        Pending = null; Running = false; Status = $"Equip stopped: {reason}";
    }
    private void Finish(EquipResultKind kind, string details, long now)
    {
        Results.Add(new(pieces[index++], kind, details)); Pending = null; nextAction = now + 350;
        if (index == pieces.Count)
        {
            Running = false;
            var have = Results.Count(r => r.Kind is EquipResultKind.Equipped or EquipResultKind.AlreadyEquipped);
            Status = $"{SetName}: {have}/{Total} pieces equipped; {Total - have} could not be equipped.";
        }
    }
}
