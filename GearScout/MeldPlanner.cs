namespace GearScout;

public sealed record MateriaOption(uint ItemId, string Name, uint Parameter, int Value, uint MinimumItemLevel,
    double[] NqRates, double[] HqRates, double RetrievalRate);
public sealed record MeldGearRules(int GuaranteedSlots, bool Advanced, uint ItemLevel,
    IReadOnlyDictionary<uint, int> BaseStats, IReadOnlyDictionary<uint, int> StatCaps,
    IReadOnlyDictionary<uint, string>? StatNames = null);
public enum MeldActionKind { Retrieve, Meld }
public sealed record MeldAction(MeldActionKind Kind, uint MateriaId, int Position, double Chance,
    IReadOnlyList<uint> Before, IReadOnlyList<uint> After);
public sealed class PieceMeldPlan(GearPiece target, OwnedItem? copy)
{
    public GearPiece Target { get; } = target;
    public OwnedItem? Copy { get; } = copy;
    public int Preserved { get; set; }
    public List<MeldAction> Actions { get; } = [];
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public string? SkipReason { get; set; }
    public bool Valid => Errors.Count == 0;
}

public static class MeldPlanner
{
    public static double Rate(MeldGearRules rules, MateriaOption materia, bool hq, int position)
    {
        if (position < 0 || position >= 5 || materia.MinimumItemLevel > rules.ItemLevel || rules.GuaranteedSlots <= 0) return 0;
        if (position < rules.GuaranteedSlots) return 1;
        var index = position - rules.GuaranteedSlots;
        var rates = hq ? materia.HqRates : materia.NqRates;
        return rules.Advanced && index < rates.Length ? rates[index] : 0;
    }

    public static PieceMeldPlan Build(GearPiece target, OwnedItem? copy, MeldGearRules? rules,
        IReadOnlyDictionary<uint, MateriaOption> materia)
    {
        var plan = new PieceMeldPlan(target, copy);
        // Relic/custom-stat data can resemble invalid materia. Socketless items cannot be
        // melded at all, so exclude them before examining targets or current materia.
        if (rules?.GuaranteedSlots == 0)
        {
            plan.SkipReason = "No materia sockets — excluded from melding.";
            if (target.Materia?.Count > 0) plan.Warnings.Add("Materia targets are ignored because this item has no sockets.");
            return plan;
        }
        if (target.Materia == null) plan.Errors.Add("Materia target is not specified. Edit targets first.");
        else if (target.Materia.Count > 5) plan.Errors.Add("A piece can have at most five materia.");
        if (copy == null) plan.Errors.Add("Gear not found in checked ownership.");
        else
        {
            if (copy.ItemId != target.ItemId) plan.Errors.Add("Selected copy is a different item.");
            if (copy.HighQuality == null) plan.Errors.Add("Quality is unknown. Reopen this storage to refresh the copy.");
            if (copy.Materia == null || copy.Materia.Count > 5) plan.Errors.Add("Current melds are unknown or invalid.");
            if (!copy.IsLive) plan.Warnings.Add("Saved storage copy: withdraw it and select its live location before guidance.");
        }
        if (rules == null) plan.Errors.Add("Item melding rules are unavailable.");
        if (!plan.Valid) return plan;
        var wanted = target.Materia!.ToList();
        var current = copy!.Materia!.ToList();
        foreach (var id in wanted.Concat(current).Distinct())
            if (!materia.ContainsKey(id)) plan.Errors.Add($"Unknown materia item {id}.");
        if (!plan.Valid) return plan;
        List<uint>? sequence = null;
        // Retrieval always removes the bottom meld. Retain the longest feasible existing prefix,
        // then choose a legal order with the lowest expected number of remaining attempts.
        for (var keep = Math.Min(current.Count, wanted.Count); keep >= 0 && sequence == null; keep--)
        {
            var remaining = wanted.ToList();
            if (!current.Take(keep).All(id => remaining.Remove(id))) continue;
            var candidates = Permutations(remaining).Where(order => order.Select((id, i) =>
                Rate(rules!, materia[id], copy.HighQuality!.Value, keep + i)).All(p => p > 0)).ToList();
            sequence = candidates.MinBy(order => order.Select((id, i) =>
                1 / Rate(rules!, materia[id], copy.HighQuality!.Value, keep + i)).Sum());
            if (sequence != null) plan.Preserved = keep;
        }
        if (sequence == null) { plan.Errors.Add("Target cannot fit the item's guaranteed slots, advanced-melding or grade restrictions."); return plan; }
        var state = current.ToList();
        while (state.Count > plan.Preserved)
        {
            var before = state.ToArray(); var id = state[^1]; state.RemoveAt(state.Count - 1);
            plan.Actions.Add(new(MeldActionKind.Retrieve, id, before.Length - 1, materia[id].RetrievalRate, before, state.ToArray()));
        }
        foreach (var id in sequence)
        {
            var before = state.ToArray(); state.Add(id);
            plan.Actions.Add(new(MeldActionKind.Meld, id, before.Length,
                Rate(rules!, materia[id], copy.HighQuality!.Value, before.Length), before, state.ToArray()));
        }
        foreach (var stat in wanted.Select(id => materia[id]).GroupBy(m => m.Parameter))
        {
            var name = rules!.StatNames?.GetValueOrDefault(stat.Key) ?? $"Parameter {stat.Key}";
            if (!rules.StatCaps.TryGetValue(stat.Key, out var cap)) { plan.Warnings.Add($"Stat cap for {name} is unknown."); continue; }
            var total = rules.BaseStats.GetValueOrDefault(stat.Key) + stat.Sum(m => m.Value);
            if (total > cap) plan.Warnings.Add($"{name}: target wastes {total - cap} points above the cap ({cap}).");
        }
        return plan;
    }

    private static IEnumerable<List<uint>> Permutations(List<uint> values)
    {
        if (values.Count == 0) { yield return []; yield break; }
        foreach (var id in values.Distinct())
        {
            var rest = values.ToList(); rest.Remove(id);
            foreach (var tail in Permutations(rest)) yield return new[] { id }.Concat(tail).ToList();
        }
    }
}

public sealed record MateriaBudget(uint MateriaId, int Minimum, double Average, int Budget,
    int Bags, int Retainers, double AvailableChance)
{
    public int Additional => Math.Max(0, Budget - Bags);
}
public sealed record MeldBudget(IReadOnlyList<MateriaBudget> Items, double CompletionChance, bool CompletePlan);

public static class MeldProbability
{
    // Exact convolution of independent geometric attempt counts. Tiny truncated tails are not
    // renormalized: CDFs and confidence budgets remain conservative rather than overstated.
    public static double[] Distribution(IReadOnlyList<double> probabilities)
    {
        if (probabilities.Any(p => !double.IsFinite(p) || p <= 0 || p > 1)) throw new ArgumentException("Invalid melding probability.");
        var size = 1 + probabilities.Sum(p => p == 1 ? 1 : (int)Math.Ceiling(Math.Log(1e-12 / Math.Max(1, probabilities.Count)) / Math.Log(1 - p)));
        if (size > 200_000) throw new ArgumentException("Melding distribution is too large.");
        var previous = new double[size]; previous[0] = 1;
        foreach (var p in probabilities)
        {
            var next = new double[size];
            for (var n = 1; n < size; n++) next[n] = p * previous[n - 1] + (1 - p) * next[n - 1];
            previous = next;
        }
        return previous;
    }

    public static int Quantile(double[] distribution, double confidence)
    {
        if (confidence <= 0 || confidence >= 1) throw new ArgumentException("Confidence must be between zero and one.");
        double cdf = 0;
        for (var n = 0; n < distribution.Length; n++) { cdf += distribution[n]; if (cdf >= confidence) return n; }
        throw new InvalidOperationException("Insufficient probability range.");
    }

    public static MeldBudget Budget(IReadOnlyList<PieceMeldPlan> plans, IReadOnlyCollection<OwnedItem> owned, double confidence)
    {
        if (confidence <= 0 || confidence >= 1) throw new ArgumentException("Invalid confidence.");
        var groups = plans.Where(p => p.Valid).SelectMany(p => p.Actions).Where(a => a.Kind == MeldActionKind.Meld)
            .GroupBy(a => a.MateriaId).ToList();
        var items = new List<MateriaBudget>(); double completion = 1;
        var perType = Math.Pow(confidence, 1.0 / Math.Max(1, groups.Count));
        foreach (var group in groups)
        {
            var probabilities = group.Select(a => a.Chance).ToArray();
            var distribution = Distribution(probabilities);
            var bags = owned.Where(i => i.ItemId == group.Key && i.IsLive && i.Container is >= 0 and <= 3).Sum(i => i.Quantity);
            var retainers = owned.Where(i => i.ItemId == group.Key && i.LastCheckedUtc != null && i.Location.StartsWith("Retainer:", StringComparison.Ordinal)).Sum(i => i.Quantity);
            var chance = distribution.Take(Math.Min(distribution.Length, bags + 1)).Sum();
            completion *= chance;
            items.Add(new(group.Key, probabilities.Length, probabilities.Sum(p => 1 / p), Quantile(distribution, perType), bags, retainers, chance));
        }
        return new(items, completion, plans.All(p => p.Valid));
    }
}
