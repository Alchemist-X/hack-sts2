namespace Sts2Sim.Diff;

public enum DiffSeverity
{
    Hard,
    Soft
}

public sealed record FieldDiff(string Path, JsonNode? Oracle, JsonNode? Sim, DiffSeverity Severity, string? Note = null)
{
    public string PathPattern => PatternOf(Path);

    public static string PatternOf(string path)
    {
        var sb = new System.Text.StringBuilder(path.Length);
        int i = 0;
        while (i < path.Length)
        {
            char c = path[i];
            if (c == '[')
            {
                int end = path.IndexOf(']', i);
                if (end > i)
                {
                    string inner = path.Substring(i + 1, end - i - 1);
                    if (inner.Length > 0 && inner.All(char.IsAsciiDigit))
                    {
                        sb.Append("[*]");
                    }
                    else if (inner.StartsWith("combat_id=", StringComparison.Ordinal))
                    {
                        sb.Append("[combat_id=*]");
                    }
                    else
                    {
                        sb.Append('[').Append(inner).Append(']');
                    }
                    i = end + 1;
                    continue;
                }
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    public JsonObject ToJson()
    {
        var obj = new JsonObject
        {
            ["path"] = Path,
            ["severity"] = Severity == DiffSeverity.Hard ? "hard" : "soft",
            ["oracle"] = Oracle?.DeepClone(),
            ["sim"] = Sim?.DeepClone()
        };
        if (Note != null)
        {
            obj["note"] = Note;
        }
        return obj;
    }
}

public static class ProjectionComparer
{
    public const string NoteMissingInSim = "absent in sim";

    public const string NoteMissingInOracle = "absent in oracle";

    public static List<FieldDiff> Compare(Projection oracle, Projection sim)
    {
        ArgumentNullException.ThrowIfNull(oracle);
        ArgumentNullException.ThrowIfNull(sim);
        var diffs = new List<FieldDiff>();
        IntField(diffs, "turn.player_turn", oracle.Turn.PlayerTurn, sim.Turn.PlayerTurn);
        StringField(diffs, "turn.phase", oracle.Turn.Phase, sim.Turn.Phase, DiffSeverity.Hard);

        var op = oracle.Player;
        var sp = sim.Player;
        IntField(diffs, "player.hp", op.Hp, sp.Hp);
        IntField(diffs, "player.max_hp", op.MaxHp, sp.MaxHp);
        IntField(diffs, "player.block", op.Block, sp.Block);
        IntField(diffs, "player.energy", op.Energy, sp.Energy);
        IntField(diffs, "player.max_energy", op.MaxEnergy, sp.MaxEnergy);
        CompareHand(diffs, op.Hand, sp.Hand);
        IntField(diffs, "player.draw_count", op.DrawCount, sp.DrawCount);
        IntField(diffs, "player.discard_count", op.DiscardCount, sp.DiscardCount);
        IntField(diffs, "player.exhaust_count", op.ExhaustCount, sp.ExhaustCount);
        ComparePowers(diffs, "player.powers", op.Powers, sp.Powers);
        CompareEnemies(diffs, oracle.Enemies, sim.Enemies);

        if (op.DrawMultiset != null && sp.DrawMultiset != null)
        {
            var o = op.DrawMultiset.OrderBy(s => s, StringComparer.Ordinal).ToList();
            var s = sp.DrawMultiset.OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (!o.SequenceEqual(s, StringComparer.Ordinal))
            {
                diffs.Add(new FieldDiff("player.draw_multiset", StringArray(o), StringArray(s), DiffSeverity.Soft, MultisetNote(o, s)));
            }
        }
        OrderedPile(diffs, "player.discard", op.Discard, sp.Discard);
        OrderedPile(diffs, "player.exhaust", op.Exhaust, sp.Exhaust);
        return diffs;
    }

    public static FieldDiff? FirstHard(IReadOnlyList<FieldDiff> diffs)
    {
        foreach (var d in diffs)
        {
            if (d.Severity == DiffSeverity.Hard)
            {
                return d;
            }
        }
        return null;
    }

    public static bool HasHard(IReadOnlyList<FieldDiff> diffs) => FirstHard(diffs) != null;

    private static void CompareHand(List<FieldDiff> diffs, IReadOnlyList<HandCardProjection> o, IReadOnlyList<HandCardProjection> s)
    {
        if (o.Count != s.Count)
        {
            diffs.Add(new FieldDiff("player.hand.length", o.Count, s.Count, DiffSeverity.Hard));
        }
        int n = Math.Min(o.Count, s.Count);
        for (int i = 0; i < n; i++)
        {
            string p = $"player.hand[{i}]";
            StringField(diffs, p + ".id", o[i].Id, s[i].Id, DiffSeverity.Hard);
            BoolField(diffs, p + ".upgraded", o[i].Upgraded, s[i].Upgraded);
            if (o[i].Cost != s[i].Cost)
            {
                diffs.Add(new FieldDiff(p + ".cost", o[i].Cost.ToJson(), s[i].Cost.ToJson(), DiffSeverity.Hard));
            }
            BoolField(diffs, p + ".can_play", o[i].CanPlay, s[i].CanPlay);
        }
    }

    private static void CompareEnemies(List<FieldDiff> diffs, IReadOnlyList<EnemyProjection> oracle, IReadOnlyList<EnemyProjection> sim)
    {
        var o = oracle.OrderBy(e => e.CombatId).ToList();
        var s = sim.OrderBy(e => e.CombatId).ToList();
        var oIds = o.Select(e => e.CombatId).ToList();
        var sIds = s.Select(e => e.CombatId).ToList();
        if (!oIds.SequenceEqual(sIds))
        {
            diffs.Add(new FieldDiff("enemies.combat_ids", IntArray(oIds), IntArray(sIds), DiffSeverity.Hard));
        }
        var sById = new Dictionary<int, EnemyProjection>();
        foreach (var e in s)
        {
            sById.TryAdd(e.CombatId, e);
        }
        var seen = new HashSet<int>();
        foreach (var oe in o)
        {
            if (!seen.Add(oe.CombatId) || !sById.TryGetValue(oe.CombatId, out var se))
            {
                continue;
            }
            string p = $"enemies[combat_id={oe.CombatId}]";
            StringField(diffs, p + ".monster_id", oe.MonsterId, se.MonsterId, DiffSeverity.Hard);
            IntField(diffs, p + ".hp", oe.Hp, se.Hp);
            IntField(diffs, p + ".max_hp", oe.MaxHp, se.MaxHp);
            IntField(diffs, p + ".block", oe.Block, se.Block);
            ComparePowers(diffs, p + ".powers", oe.Powers, se.Powers);
            CompareIntents(diffs, p + ".intents", oe.Intents, se.Intents);
        }
    }

    private static void CompareIntents(List<FieldDiff> diffs, string path, IReadOnlyList<IntentProjection> o, IReadOnlyList<IntentProjection> s)
    {
        if (o.Count != s.Count)
        {
            diffs.Add(new FieldDiff(path + ".length", o.Count, s.Count, DiffSeverity.Hard));
        }
        int n = Math.Min(o.Count, s.Count);
        for (int i = 0; i < n; i++)
        {
            string p = $"{path}[{i}]";
            var oi = o[i];
            var si = s[i];
            StringField(diffs, p + ".type", oi.Type, si.Type, DiffSeverity.Hard);
            NullableIntField(diffs, p + ".attack_value", oi.AttackValue, si.AttackValue);
            NullableIntField(diffs, p + ".attack_times", oi.AttackTimes, si.AttackTimes);
            foreach (var key in oi.Extras.Keys.Union(si.Extras.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
            {
                bool inO = oi.Extras.TryGetValue(key, out var ov);
                bool inS = si.Extras.TryGetValue(key, out var sv);
                if (inO && inS)
                {
                    if (!JsonNode.DeepEquals(ov, sv))
                    {
                        diffs.Add(new FieldDiff($"{p}.{key}", ov?.DeepClone(), sv?.DeepClone(), DiffSeverity.Soft));
                    }
                }
                else
                {
                    diffs.Add(new FieldDiff($"{p}.{key}", inO ? ov?.DeepClone() : null, inS ? sv?.DeepClone() : null, DiffSeverity.Soft,
                        inO ? NoteMissingInSim : NoteMissingInOracle));
                }
            }
        }
    }

    private static void ComparePowers(List<FieldDiff> diffs, string path, IReadOnlyList<PowerProjection> o, IReadOnlyList<PowerProjection> s)
    {
        var oGroups = Group(o, out var oOrder);
        var sGroups = Group(s, out var sOrder);
        var ids = new List<string>(oOrder);
        foreach (var id in sOrder)
        {
            if (!oGroups.ContainsKey(id))
            {
                ids.Add(id);
            }
        }
        bool presenceDiffers = false;
        foreach (var id in ids)
        {
            oGroups.TryGetValue(id, out var oa);
            sGroups.TryGetValue(id, out var sa);
            int on = oa?.Count ?? 0;
            int sn = sa?.Count ?? 0;
            for (int k = 0; k < Math.Max(on, sn); k++)
            {
                string p = k == 0 ? $"{path}[{id}]" : $"{path}[{id}#{k + 1}]";
                if (k < on && k < sn)
                {
                    if (oa![k] != sa![k])
                    {
                        diffs.Add(new FieldDiff(p + ".amount", oa[k], sa[k], DiffSeverity.Hard));
                    }
                }
                else if (k < on)
                {
                    presenceDiffers = true;
                    diffs.Add(new FieldDiff(p, oa![k], null, DiffSeverity.Hard, NoteMissingInSim));
                }
                else
                {
                    presenceDiffers = true;
                    diffs.Add(new FieldDiff(p, null, sa![k], DiffSeverity.Hard, NoteMissingInOracle));
                }
            }
        }
        if (!presenceDiffers)
        {
            var oSeq = o.Select(x => x.Id).ToList();
            var sSeq = s.Select(x => x.Id).ToList();
            if (!oSeq.SequenceEqual(sSeq, StringComparer.Ordinal))
            {
                diffs.Add(new FieldDiff(path + ".order", StringArray(oSeq), StringArray(sSeq), DiffSeverity.Soft));
            }
        }
    }

    private static Dictionary<string, List<int>> Group(IReadOnlyList<PowerProjection> powers, out List<string> order)
    {
        var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        order = new List<string>();
        foreach (var pw in powers)
        {
            if (!groups.TryGetValue(pw.Id, out var list))
            {
                list = new List<int>(1);
                groups[pw.Id] = list;
                order.Add(pw.Id);
            }
            list.Add(pw.Amount);
        }
        return groups;
    }

    private static void OrderedPile(List<FieldDiff> diffs, string path, IReadOnlyList<string>? o, IReadOnlyList<string>? s)
    {
        if (o == null || s == null)
        {
            return;
        }
        if (!o.SequenceEqual(s, StringComparer.Ordinal))
        {
            var os = o.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var ss = s.OrderBy(x => x, StringComparer.Ordinal).ToList();
            string note = os.SequenceEqual(ss, StringComparer.Ordinal) ? "same cards, different order" : MultisetNote(os, ss);
            diffs.Add(new FieldDiff(path, StringArray(o), StringArray(s), DiffSeverity.Soft, note));
        }
    }

    private static string MultisetNote(IReadOnlyList<string> oracleSorted, IReadOnlyList<string> simSorted)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var x in oracleSorted)
        {
            counts[x] = counts.GetValueOrDefault(x) + 1;
        }
        foreach (var x in simSorted)
        {
            counts[x] = counts.GetValueOrDefault(x) - 1;
        }
        var missing = counts.Where(kv => kv.Value > 0).Select(kv => kv.Value == 1 ? kv.Key : $"{kv.Key}x{kv.Value}");
        var extra = counts.Where(kv => kv.Value < 0).Select(kv => kv.Value == -1 ? kv.Key : $"{kv.Key}x{-kv.Value}");
        return $"missing in sim: [{string.Join(", ", missing)}]; extra in sim: [{string.Join(", ", extra)}]";
    }

    private static void IntField(List<FieldDiff> diffs, string path, int o, int s)
    {
        if (o != s)
        {
            diffs.Add(new FieldDiff(path, o, s, DiffSeverity.Hard));
        }
    }

    private static void NullableIntField(List<FieldDiff> diffs, string path, int? o, int? s)
    {
        if (o == s)
        {
            return;
        }
        string? note = o is null ? NoteMissingInOracle : s is null ? NoteMissingInSim : null;
        diffs.Add(new FieldDiff(path, o is int ov ? JsonValue.Create(ov) : null, s is int sv ? JsonValue.Create(sv) : null, DiffSeverity.Hard, note));
    }

    private static void BoolField(List<FieldDiff> diffs, string path, bool o, bool s)
    {
        if (o != s)
        {
            diffs.Add(new FieldDiff(path, o, s, DiffSeverity.Hard));
        }
    }

    private static void StringField(List<FieldDiff> diffs, string path, string o, string s, DiffSeverity severity)
    {
        if (!string.Equals(o, s, StringComparison.Ordinal))
        {
            diffs.Add(new FieldDiff(path, o, s, severity));
        }
    }

    private static JsonArray StringArray(IEnumerable<string> items)
    {
        var a = new JsonArray();
        foreach (var s in items)
        {
            a.Add(s);
        }
        return a;
    }

    private static JsonArray IntArray(IEnumerable<int> items)
    {
        var a = new JsonArray();
        foreach (var i in items)
        {
            a.Add(i);
        }
        return a;
    }
}
