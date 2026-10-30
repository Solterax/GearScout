namespace GearScout;

public static class MeldEligibility
{
    public static bool CanGuide(PieceMeldPlan plan) => plan.Valid && plan.SkipReason == null && plan.Copy?.IsLive == true && plan.Actions.Count > 0;

    public static string Reason(PieceMeldPlan plan) => plan.SkipReason ??
        (plan.Errors.Count > 0 ? string.Join(" ", plan.Errors) : plan.Copy?.IsLive != true ? "Withdraw the gear and choose its live copy." :
            plan.Actions.Count == 0 ? "Already matches its materia targets." : "Ready for guidance.");

    public static List<PieceStatus> SuggestCopies(IEnumerable<GearPiece> pieces, IReadOnlyCollection<OwnedItem> owned)
    {
        var targets = pieces.ToList();
        var live = GearMatcher.Match(targets, owned.Where(i => i.IsLive));
        var saved = GearMatcher.Match(live.Where(s => s.Match == null).Select(s => s.Piece), owned.Where(i => !i.IsLive));
        return live.Select(s => s.Match != null ? s : saved.First(c => ReferenceEquals(c.Piece, s.Piece))).ToList();
    }
}
