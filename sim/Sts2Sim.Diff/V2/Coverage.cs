namespace Sts2Sim.Diff.V2;

public sealed class CoverageEntry
{
    public int TouchedDecisions { get; set; }

    public int AgreedDecisions { get; set; }

    public int FirstDivergences { get; set; }

    public HashSet<string> Lineages { get; } = new(StringComparer.Ordinal);

    public HashSet<string> AgreedLineages { get; } = new(StringComparer.Ordinal);

    public bool MeetsAgreementMinimum => AgreedDecisions >= CoverageCounter.MinAgreedDecisions && AgreedLineages.Count >= CoverageCounter.MinLineages;
}

public sealed class CoverageCounter
{
    public const int MinAgreedDecisions = 3;
    public const int MinLineages = 2;

    public SortedDictionary<string, SortedDictionary<string, CoverageEntry>> Areas { get; } = new(StringComparer.Ordinal);

    public long DecisionsReached { get; private set; }

    public long DecisionsUnobserved { get; private set; }

    public static CoverageCounter From(IEnumerable<SegmentResult> results)
    {
        var c = new CoverageCounter();
        foreach (var r in results)
        {
            c.Add(r);
        }
        return c;
    }

    public void Add(SegmentResult r)
    {
        if (r.Status is BundleStatus.Skipped or BundleStatus.Error)
        {
            return;
        }
        DecisionsUnobserved += r.DecisionsUnobserved;
        var agreed = new HashSet<int>(r.AgreedIndices);
        foreach (var (index, keys) in r.Touched)
        {
            DecisionsReached++;
            bool isAgreed = agreed.Contains(index);
            bool isDivergence = r.Divergence != null && r.Divergence.DecisionIndex == index;
            foreach (var key in keys)
            {
                var entry = Get(key);
                entry.TouchedDecisions++;
                entry.Lineages.Add(r.Lineage);
                if (isAgreed)
                {
                    entry.AgreedDecisions++;
                    entry.AgreedLineages.Add(r.Lineage);
                }
                if (isDivergence)
                {
                    entry.FirstDivergences++;
                }
            }
        }
    }

    private CoverageEntry Get(ContentKey key)
    {
        if (!Areas.TryGetValue(key.Area, out var ids))
        {
            ids = new SortedDictionary<string, CoverageEntry>(StringComparer.Ordinal);
            Areas[key.Area] = ids;
        }
        if (!ids.TryGetValue(key.Id, out var entry))
        {
            entry = new CoverageEntry();
            ids[key.Id] = entry;
        }
        return entry;
    }

    public JsonObject ToJson(bool perId = true)
    {
        var areas = new JsonObject();
        foreach (var (area, ids) in Areas)
        {
            var items = new JsonObject();
            int met = 0;
            foreach (var (id, e) in ids)
            {
                if (e.MeetsAgreementMinimum)
                {
                    met++;
                }
                if (perId)
                {
                    items[id] = new JsonObject
                    {
                        ["touched"] = e.TouchedDecisions,
                        ["agreed"] = e.AgreedDecisions,
                        ["lineages"] = e.Lineages.Count,
                        ["agreed_lineages"] = e.AgreedLineages.Count,
                        ["first_divergences"] = e.FirstDivergences,
                        ["ported"] = ContentIndex.IsPorted(area, id),
                        ["agreement_minimum_met"] = e.MeetsAgreementMinimum
                    };
                }
            }
            var obj = new JsonObject { ["items"] = ids.Count, ["agreement_minimum_met"] = met };
            if (perId)
            {
                obj["ids"] = items;
            }
            areas[area] = obj;
        }
        return new JsonObject
        {
            ["rule"] = $"agreement_minimum_met = >= {MinAgreedDecisions} agreeing touched decisions in >= {MinLineages} lineages (hook coverage not measured)",
            ["decisions_reached"] = DecisionsReached,
            ["decisions_unobserved"] = DecisionsUnobserved,
            ["unobserved_rate"] = DecisionsReached == 0 ? 0.0 : (double)DecisionsUnobserved / DecisionsReached,
            ["areas"] = areas
        };
    }
}
