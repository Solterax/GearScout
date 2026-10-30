namespace GearScout;

public sealed record MeldGuideStep(OwnedItem Copy, MeldAction Action);

// Guidance observes actions performed in the game's own dialogs. It never confirms a meld or
// retrieval. Limits stop this session; they cannot block manual actions or native batch melding.
public sealed class MeldGuide
{
    private List<MeldGuideStep> steps = [];
    private Dictionary<uint, int> previousStock = [];
    private Dictionary<uint, int> limits = [];
    private readonly Dictionary<uint, int> spent = [];
    private ulong owner;
    private long lastProgress;
    public bool Running { get; private set; }
    public int Completed { get; private set; }
    public int Total => steps.Count;
    public int Consumed => spent.Values.Sum();
    public int Limit { get; private set; }
    public MeldGuideStep? Current => Running && Completed < steps.Count ? steps[Completed] : null;
    public string Status { get; private set; } = "Start guidance to follow the removal and melding sequence in game.";

    public void Start(ulong characterId, IReadOnlyList<PieceMeldPlan> plans, IReadOnlyCollection<OwnedItem> owned,
        IReadOnlyDictionary<uint, int> materialLimits, int totalLimit, long now)
    {
        if (characterId == 0 || plans.Count == 0 || plans.Any(p => !p.Valid || p.Copy is not { IsLive: true }))
            throw new ArgumentException("Guidance requires complete live gear copies and valid targets.");
        if (plans.Select(p => p.Copy!.CopyKey).Distinct().Count() != plans.Count) throw new ArgumentException("A physical copy cannot fill two target slots.");
        if (totalLimit < 0 || materialLimits.Values.Any(n => n < 0)) throw new ArgumentException("Material limits cannot be negative.");
        owner = characterId;
        steps = plans.SelectMany(p => p.Actions.Select(a => new MeldGuideStep(p.Copy!, a))).ToList();
        limits = materialLimits.ToDictionary(); spent.Clear(); previousStock = Stock(owned);
        Limit = totalLimit; Completed = 0; lastProgress = now; Running = steps.Count > 0;
        Status = Running ? "Perform the highlighted next action in the game window." : "All selected gear already matches its targets.";
    }

    public void Stop(string reason = "Guidance stopped. Your completed changes are kept.") { Running = false; Status = reason; }

    public void Observe(ulong characterId, bool ready, IReadOnlyCollection<OwnedItem>? owned, long now)
    {
        if (!Running) return;
        if (!ready || characterId != owner || owned == null) { Stop("Guidance stopped: character, loading or inventory changed."); return; }
        var stock = Stock(owned);
        foreach (var id in limits.Keys)
        {
            var used = Math.Max(0, previousStock.GetValueOrDefault(id) - stock.GetValueOrDefault(id));
            if (used > 0) { spent[id] = spent.GetValueOrDefault(id) + used; lastProgress = now; }
        }
        previousStock = stock;
        var step = Current!;
        var copy = owned.FirstOrDefault(i => i.IsLive && i.CopyKey == step.Copy.CopyKey);
        if (copy == null || copy.ItemId != step.Copy.ItemId || copy.HighQuality != step.Copy.HighQuality || copy.Materia == null)
        { Stop("Guidance stopped: the selected gear copy moved or changed. Select it again and replan."); return; }
        if (copy.Materia.SequenceEqual(step.Action.After)) { Completed++; lastProgress = now; }
        else if (!copy.Materia.SequenceEqual(step.Action.Before)) { Stop("Guidance stopped: unexpected meld change. Review the updated plan."); return; }
        if (limits.Any(p => spent.GetValueOrDefault(p.Key) > p.Value) || Consumed > Limit)
        { Stop("Material limit reached. Guidance stopped; review stock before continuing."); return; }
        if (Completed == steps.Count) { Stop("Melding plan complete."); return; }
        if (Consumed >= Limit && Current!.Action.Kind == MeldActionKind.Meld)
        { Stop("Material limit reached. Guidance stopped; review stock before continuing."); return; }
        if (Current!.Action.Kind == MeldActionKind.Meld && spent.GetValueOrDefault(Current.Action.MateriaId) >= limits.GetValueOrDefault(Current.Action.MateriaId))
        { Stop("Limit for the next materia type reached. Guidance stopped."); return; }
        if (Current!.Action.Kind == MeldActionKind.Meld && stock.GetValueOrDefault(Current.Action.MateriaId) == 0)
        { Stop("Next materia is unavailable in bags. Guidance stopped."); return; }
        if (now - lastProgress > 180_000) Stop("Guidance timed out. Review the current plan before restarting.");
    }

    private static Dictionary<uint, int> Stock(IEnumerable<OwnedItem> owned) => owned
        .Where(i => i.IsLive && i.Container is >= 0 and <= 3 && i.Quantity > 0).GroupBy(i => i.ItemId)
        .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
}
