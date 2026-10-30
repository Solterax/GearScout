namespace GearScout;

public static class ZoneEquipRules
{
    public static void Assign(IReadOnlyCollection<TrackedSet> sets, TrackedSet target, uint territory)
    {
        if (territory == 0 || target.IsGlam || target.Pieces.Count == 0 || !sets.Contains(target))
            throw new ArgumentException("Select a saved normal gear set with gear and a valid duty or zone.");
        // One assignment per job/territory; other jobs can use their own sets in the same duty.
        foreach (var set in sets.Where(s => !s.IsGlam && string.Equals(s.Job, target.Job, StringComparison.OrdinalIgnoreCase)))
            set.AutoEquipTerritories.RemoveAll(id => id == territory);
        target.AutoEquipTerritories.Add(territory);
    }
}

public sealed class ZoneEquipSession
{
    private uint territory;
    private ulong owner;
    private long enteredAt;
    private long? stableSince;
    public bool Pending { get; private set; }
    public string Status { get; private set; } = "";

    public void Enter(uint territoryId, ulong characterId, long now)
    {
        territory = territoryId; owner = characterId; enteredAt = now; stableSince = null;
        Pending = territoryId != 0; Status = "";
    }

    public void Cancel() { Pending = false; stableSince = null; Status = ""; }

    public TrackedSet? Tick(uint currentTerritory, ulong characterId, string job, bool ready, bool blocked,
        IReadOnlyCollection<TrackedSet> sets, long now)
    {
        if (!Pending) return null;
        if ((currentTerritory != 0 && currentTerritory != territory) ||
            (owner != 0 && characterId != 0 && owner != characterId)) { Cancel(); return null; }
        if (now - enteredAt >= 180_000)
        {
            Cancel(); Status = "Auto-equip skipped: loading or another action did not finish within three minutes.";
            return null;
        }
        if (!ready || blocked || currentTerritory == 0 || characterId == 0 || string.IsNullOrEmpty(job)) { stableSince = null; return null; }
        if (owner == 0) owner = characterId;
        stableSince ??= now;
        if (now - stableSince.Value < 2000) return null;
        var candidates = sets.Where(s => !s.IsGlam && s.Pieces.Count > 0 &&
            string.Equals(s.Job, job, StringComparison.OrdinalIgnoreCase) && s.AutoEquipTerritories.Contains(territory)).ToList();
        Pending = false;
        if (candidates.Count > 1)
        {
            Status = "Auto-equip skipped: multiple sets are assigned to this job and zone. Reassign the zone to one set.";
            return null;
        }
        if (candidates.Count == 0) return null;
        Status = $"Auto-equipping {candidates[0].Name} on zone entry.";
        return candidates[0];
    }
}
