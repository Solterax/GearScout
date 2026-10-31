namespace GearScout;

public sealed class SetMatchCache
{
    private TrackedSet? previousSet;
    private IReadOnlyCollection<OwnedItem>? previousOwned;
    private long previousRevision;
    private List<PieceStatus> statuses = [];
    public void Clear() { previousSet = null; previousOwned = null; statuses = []; }

    // Revisions invalidate in-place target edits; ownership identities change only after a verified difference.
    public List<PieceStatus> Get(TrackedSet set, IReadOnlyCollection<OwnedItem> owned, long revision)
    {
        if (ReferenceEquals(previousSet, set) && ReferenceEquals(previousOwned, owned) && previousRevision == revision) return statuses;
        IEnumerable<GearPiece> pieces = set.Pieces;
        if (set.IsGlam) pieces = pieces.OrderBy(p => Array.IndexOf(GearSearch.Slots, p.Slot));
        statuses = GearMatcher.Match(pieces, owned, set.IsGlam);
        previousSet = set; previousOwned = owned; previousRevision = revision;
        return statuses;
    }
}
