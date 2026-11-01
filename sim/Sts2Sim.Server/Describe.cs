using System.Globalization;
using System.Text;
using Sts2Sim.Diff;

namespace Sts2Sim.Server;

// Small, exception-tolerant helpers shared by the token mapper and the observation builder.
public static class Describe
{
    public static string Snake(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1]))))
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public static string CardName(CardInstance card)
        => card.UpgradeLevel switch
        {
            0 => card.Id,
            1 => card.Id + "+",
            var n => card.Id + "+" + n.ToString(CultureInfo.InvariantCulture)
        };

    public static string OptionName(ChoiceOption option)
        => option.UpgradeLevel switch
        {
            0 => option.ModelId,
            1 => option.ModelId + "+",
            var n => option.ModelId + "+" + n.ToString(CultureInfo.InvariantCulture)
        };

    public static SortedDictionary<string, int> Vars(CardInstance card)
    {
        var result = new SortedDictionary<string, int>(StringComparer.Ordinal);
        try
        {
            var all = card.Vars.All;
            for (int i = 0; i < all.Count; i++)
            {
                var v = all[i];
                result[Snake(v.Name)] = v.IntValue;
            }
        }
        catch (Exception)
        {
        }
        return result;
    }

    public static string VarSummary(CardInstance card)
    {
        var vars = Vars(card);
        var parts = new List<string>(vars.Count);
        foreach (var (name, value) in vars)
        {
            parts.Add(name switch
            {
                "damage" => value.ToString(CultureInfo.InvariantCulture) + " dmg",
                "block" => value.ToString(CultureInfo.InvariantCulture) + " block",
                _ => name + "=" + value.ToString(CultureInfo.InvariantCulture)
            });
        }
        return string.Join(", ", parts);
    }

    public static JsonNode CostNode(CombatState? cs, CardInstance card)
    {
        try
        {
            if (card.EnergyCost.CostsX)
            {
                return JsonValue.Create("X")!;
            }
            int cost = cs != null ? card.EnergyCost.GetAmountToSpend(cs, card) : card.EnergyCost.GetAmountToSpend();
            return JsonValue.Create(cost)!;
        }
        catch (Exception)
        {
            return JsonValue.Create(card.EnergyCost.Canonical)!;
        }
    }

    public static string CostText(CombatState? cs, CardInstance card)
    {
        var node = CostNode(cs, card);
        return node.GetValueKind() == JsonValueKind.String ? "Xe" : node.GetValue<int>().ToString(CultureInfo.InvariantCulture) + "e";
    }

    public static int? StarCost(CombatState? cs, CardInstance card)
    {
        try
        {
            if (card.Def.CanonicalStarCost < 0 && !card.Def.HasStarCostX)
            {
                return null;
            }
            return cs != null ? card.GetStarCostWithModifiers(cs) : card.CurrentStarCost;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static List<string> Keywords(CombatState? cs, CardInstance card)
    {
        var list = new List<string>();
        try
        {
            foreach (var k in card.Keywords(cs).OrderBy(k => k))
            {
                list.Add(Snake(k.ToString()));
            }
        }
        catch (Exception)
        {
        }
        return list;
    }

    public static bool Playable(CombatState cs, CardInstance card, out string? reason)
    {
        try
        {
            bool ok = card.CanPlay(cs, out var why);
            if (ok && why == UnplayableReason.None)
            {
                reason = null;
                return true;
            }
            reason = why == UnplayableReason.None ? "unplayable" : why.ToString();
            return false;
        }
        catch (Exception ex)
        {
            reason = "error: " + ex.GetType().Name;
            return false;
        }
    }

    public static List<(int Index, MonsterState Monster)> LiveEnemies(CombatState cs)
    {
        var list = new List<(int, MonsterState)>();
        var monsters = cs.Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            var m = monsters[i];
            if (m.IsEnemy && m.IsAlive)
            {
                list.Add((i, m));
            }
        }
        return list;
    }

    public static List<IntentProjection> Intents(CombatState cs, MonsterState m)
    {
        var list = new List<IntentProjection>();
        try
        {
            var move = m.HasAi ? m.Ai.NextMove : null;
            if (move == null)
            {
                return list;
            }
            var targets = MonsterLifecycle.PlayerCreatures(cs);
            foreach (var intent in move.Intents)
            {
                try
                {
                    list.Add(SimProjector.Intent(cs, m, intent, targets));
                }
                catch (Exception)
                {
                    list.Add(new IntentProjection { Type = intent.IntentType.ToString() });
                }
            }
        }
        catch (Exception)
        {
        }
        return list;
    }

    public static string IntentText(IntentProjection intent)
    {
        if (intent.AttackValue is { } dmg)
        {
            int times = intent.AttackTimes ?? 1;
            return times > 1
                ? $"{intent.Type} {dmg}x{times}"
                : $"{intent.Type} {dmg}";
        }
        if (intent.Extras.TryGetValue(SimProjector.StatusCardCountKey, out var count) && count != null)
        {
            return $"{intent.Type}({count} cards)";
        }
        return intent.Type;
    }

    public static List<(string Id, int Amount)> Powers(CombatState cs, CreatureState creature)
    {
        var list = new List<(string, int)>();
        var powers = creature.Powers;
        for (int i = 0; i < powers.Count; i++)
        {
            var power = powers[i];
            bool visible;
            try
            {
                visible = SimProjector.IsVisible(cs, power);
            }
            catch (Exception)
            {
                visible = true;
            }
            if (visible)
            {
                list.Add((power.Id, power.Amount));
            }
        }
        return list;
    }

    public static string PowersText(IEnumerable<(string Id, int Amount)> powers)
        => string.Join(", ", powers.Select(p => p.Id + " " + p.Amount.ToString(CultureInfo.InvariantCulture)));

    public static string MonsterName(CombatState cs, int index)
        => index >= 0 && index < cs.Monsters.Count ? cs.Monsters[index].MonsterId : "?";

    public static string DeckSummary(IReadOnlyList<CardInstance> deck)
    {
        var counts = new List<(string Name, int Count)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < deck.Count; i++)
        {
            string name = CardName(deck[i]);
            if (index.TryGetValue(name, out int k))
            {
                counts[k] = (name, counts[k].Count + 1);
            }
            else
            {
                index[name] = counts.Count;
                counts.Add((name, 1));
            }
        }
        return string.Join(", ", counts.Select(c => c.Count > 1 ? $"{c.Name} x{c.Count}" : c.Name));
    }
}
