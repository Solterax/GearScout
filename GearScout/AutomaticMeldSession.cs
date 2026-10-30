namespace GearScout;

public enum AutoMeldPhase { Idle, Window, Category, Gear, Materia, Dialog, Result }
public enum AutoMeldCommand { None, OpenWindow, SelectCategory, SelectGear, RequestRetrieve, SelectMateria, Confirm }
public sealed record AutoMeldView(ulong Owner, bool Ready, IReadOnlyCollection<OwnedItem>? Owned,
    bool WindowReady, bool AgentReady, bool CategoryMatches, bool GearMatches, bool MateriaMatches,
    bool DialogOpen, bool DialogMatches, bool RateMatches, bool Busy, bool UnexpectedDialog);

public sealed class AutomaticMeldSession(MeldGuide guide)
{
    private ulong owner;
    private long phaseSince, nextAction, stableSince;
    private string lastResult = "";
    private Dictionary<uint, int> attemptStock = [];
    private Dictionary<uint, int> limits = [];
    private readonly Dictionary<uint, int> authorized = [];
    public AutoMeldPhase Phase { get; private set; }
    public bool Running => guide.Running && Phase != AutoMeldPhase.Idle;
    public int AttemptsAuthorized => authorized.Values.Sum();
    public string Status { get; private set; } = "Ready for automatic retrieval and melding.";

    public void Start(ulong characterId, IReadOnlyList<PieceMeldPlan> plans, IReadOnlyCollection<OwnedItem> owned,
        IReadOnlyDictionary<uint, int> materialLimits, int totalLimit, long now)
    {
        if (Running) throw new InvalidOperationException("Automatic melding is already running.");
        var needed = plans.Where(p => p.Valid && p.SkipReason == null).SelectMany(p => p.Actions)
            .Where(a => a.Kind == MeldActionKind.Meld).GroupBy(a => a.MateriaId).ToList();
        var stock = Stock(owned);
        if (totalLimit < needed.Sum(g => g.Count())) throw new ArgumentException("Material limit is below the minimum number of required melds.");
        foreach (var group in needed)
            if (stock.GetValueOrDefault(group.Key) < group.Count() || materialLimits.GetValueOrDefault(group.Key) < group.Count())
                throw new ArgumentException($"Materia {group.Key}: put at least {group.Count()} in bags and allow that many attempts before starting.");
        guide.Start(characterId, plans, owned, materialLimits, totalLimit, now);
        owner = characterId; limits = materialLimits.ToDictionary(); authorized.Clear();
        SetPhase(guide.Running ? AutoMeldPhase.Window : AutoMeldPhase.Idle, now);
        Status = guide.Running ? "Opening melding for the next planned action." : guide.Status;
    }

    public void Stop(string reason = "Automatic melding stopped. An already confirmed attempt may still finish.")
    { guide.Stop(reason); Phase = AutoMeldPhase.Idle; Status = reason; }

    public AutoMeldCommand Tick(AutoMeldView view, long now)
    {
        if (!Running) { Phase = AutoMeldPhase.Idle; return AutoMeldCommand.None; }
        if (!view.Ready || view.Owner != owner || view.Owned == null) return Fail("Character, loading or inventory changed.");
        if (view.UnexpectedDialog || view.DialogOpen && Phase is not (AutoMeldPhase.Dialog or AutoMeldPhase.Result))
            return Fail("Unexpected dialog. Close it and review the plan before restarting.");
        if (now - phaseSince > 25_000) return Fail($"Timed out waiting for {Phase}.");
        var step = guide.Current!;
        var current = view.Owned.FirstOrDefault(i => i.IsLive && i.CopyKey == step.Copy.CopyKey);
        if (current == null || current.ItemId != step.Copy.ItemId || current.HighQuality != step.Copy.HighQuality || current.Materia == null)
            return Fail("The selected gear copy moved, changed or became unavailable.");
        var before = current.Materia.SequenceEqual(step.Action.Before);
        var after = current.Materia.SequenceEqual(step.Action.After);
        if (!before && !(after && Phase is AutoMeldPhase.Dialog or AutoMeldPhase.Result))
            return Fail("Unexpected materia change on the selected gear.");
        if (now < nextAction) return AutoMeldCommand.None;
        if (Phase is not (AutoMeldPhase.Dialog or AutoMeldPhase.Result) && view.Busy) return AutoMeldCommand.None;

        switch (Phase)
        {
            case AutoMeldPhase.Window:
                if (!view.WindowReady) { SetPhase(AutoMeldPhase.Category, now); return AutoMeldCommand.OpenWindow; }
                SetPhase(AutoMeldPhase.Category, now);
                break;
            case AutoMeldPhase.Category:
                if (!view.WindowReady || !view.AgentReady) break;
                if (!view.CategoryMatches) { SetPhase(AutoMeldPhase.Gear, now); return AutoMeldCommand.SelectCategory; }
                SetPhase(AutoMeldPhase.Gear, now);
                break;
            case AutoMeldPhase.Gear:
                if (!view.WindowReady || !view.AgentReady) break;
                if (!view.CategoryMatches) return Fail("Inventory category changed unexpectedly.");
                if (!view.GearMatches) { SetPhase(AutoMeldPhase.Materia, now); return AutoMeldCommand.SelectGear; }
                SetPhase(AutoMeldPhase.Materia, now);
                break;
            case AutoMeldPhase.Materia:
                if (!view.AgentReady) break;
                if (!view.GearMatches || !view.CategoryMatches) return Fail("Selected gear changed before the next action.");
                var stock = Stock(view.Owned);
                if (step.Action.Kind == MeldActionKind.Meld)
                {
                    if (AttemptsAuthorized >= guide.Limit || authorized.GetValueOrDefault(step.Action.MateriaId) >= limits.GetValueOrDefault(step.Action.MateriaId))
                        return Fail("Material limit reached before requesting another meld.");
                    if (stock.GetValueOrDefault(step.Action.MateriaId) <= 0) return Fail("Next materia is missing from bags.");
                    // Reserve before opening the dialog, since another plugin may auto-confirm it.
                    authorized[step.Action.MateriaId] = authorized.GetValueOrDefault(step.Action.MateriaId) + 1;
                }
                attemptStock = stock; lastResult = ""; stableSince = now;
                SetPhase(AutoMeldPhase.Dialog, now);
                Status = step.Action.Kind == MeldActionKind.Retrieve ? "Opening the planned materia retrieval." : "Selecting the next materia for a single attempt.";
                return step.Action.Kind == MeldActionKind.Retrieve ? AutoMeldCommand.RequestRetrieve : AutoMeldCommand.SelectMateria;
            case AutoMeldPhase.Dialog:
                if (after || Stock(view.Owned).GetValueOrDefault(step.Action.MateriaId) != attemptStock.GetValueOrDefault(step.Action.MateriaId))
                {
                    SetPhase(AutoMeldPhase.Result, now); Status = "Waiting for an already started attempt to settle."; break;
                }
                if (view.Busy && !view.DialogOpen)
                {
                    SetPhase(AutoMeldPhase.Result, now); Status = "Waiting for the current attempt to finish."; break;
                }
                if (!view.GearMatches || !view.CategoryMatches) return Fail("Selected gear changed while opening confirmation.");
                if (view.DialogOpen)
                {
                    if (!view.DialogMatches || !view.RateMatches || step.Action.Kind == MeldActionKind.Meld && !view.MateriaMatches)
                        return Fail("Confirmation does not match the planned gear, materia or success rate.");
                    SetPhase(AutoMeldPhase.Result, now);
                    Status = "Single attempt confirmed; waiting for the game result.";
                    return AutoMeldCommand.Confirm;
                }
                break;
            case AutoMeldPhase.Result:
                var actualStock = Stock(view.Owned);
                var fingerprint = string.Join(',', current.Materia) + "|" + string.Join(';', actualStock.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value}"));
                if (view.Busy || view.DialogOpen) { lastResult = fingerprint; stableSince = now; break; }
                if (fingerprint != lastResult) { lastResult = fingerprint; stableSince = now; break; }
                if (now - stableSince < 750 || now - phaseSince < 2000) break;
                var delta = actualStock.GetValueOrDefault(step.Action.MateriaId) - attemptStock.GetValueOrDefault(step.Action.MateriaId);
                if (limits.Keys.Any(id => id != step.Action.MateriaId && actualStock.GetValueOrDefault(id) != attemptStock.GetValueOrDefault(id)))
                    return Fail("Other planned materia stock changed during the attempt.");
                if (step.Action.Kind == MeldActionKind.Meld)
                {
                    if (delta == 0 && before) break; // No acknowledged result yet; never blindly resend.
                    if (delta != -1 || !after && !(before && step.Action.Chance < 1))
                        return Fail("Unexpected meld result or consumption count. No further attempts issued.");
                }
                else
                {
                    if (before && delta == 0) break;
                    if (!after || delta is < 0 or > 1) return Fail("Unexpected materia retrieval result.");
                }
                guide.Observe(owner, true, view.Owned, now);
                if (!guide.Running) { Phase = AutoMeldPhase.Idle; Status = guide.Status; }
                else { SetPhase(AutoMeldPhase.Window, now); Status = after ? "Result verified; continuing to the next action." : "Failed overmeld verified; retrying within the material limit."; }
                break;
        }
        return AutoMeldCommand.None;
    }

    private AutoMeldCommand Fail(string reason) { Stop("Automatic melding stopped: " + reason); return AutoMeldCommand.None; }
    private void SetPhase(AutoMeldPhase phase, long now) { Phase = phase; phaseSince = now; nextAction = now + 300; }
    private static Dictionary<uint, int> Stock(IEnumerable<OwnedItem> owned) => owned.Where(i => i.IsLive && i.Container is >= 0 and <= 3 && i.Quantity > 0)
        .GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
}
