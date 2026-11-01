using System.Globalization;

namespace Sts2Sim.Server;

// Before/after snapshot of the few numbers a player watches, turned into "log" lines for a step response.
public static class StepLog
{
    public sealed record Snapshot(
        string Phase,
        int? Act,
        int? Floor,
        int? Turn,
        int Hp,
        int MaxHp,
        int Block,
        int Gold,
        List<string> Deck,
        List<string> Relics,
        List<string?> Potions,
        Dictionary<int, (string Id, int Hp, int Block)> Enemies);

    public static Snapshot Capture(GameSession g)
    {
        var p = g.Player;
        var rs = g.Run?.State;
        var cs = g.LiveCombat;
        var enemies = new Dictionary<int, (string, int, int)>();
        if (cs != null)
        {
            var monsters = cs.Monsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                if (monsters[i].IsEnemy)
                {
                    enemies[i] = (monsters[i].MonsterId, monsters[i].CurrentHp, monsters[i].Block);
                }
            }
        }
        return new Snapshot(
            g.PhaseName,
            rs != null ? rs.CurrentActIndex + 1 : null,
            rs?.TotalFloor,
            cs?.Player.TurnNumber,
            p.CurrentHp,
            p.MaxHp,
            p.Block,
            p.Gold,
            p.Deck.Select(Describe.CardName).ToList(),
            p.Relics.Select(r => r.Id).ToList(),
            p.PotionSlots.Select(s => s?.Id).ToList(),
            enemies);
    }

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    public static List<string> Diff(Snapshot a, Snapshot b)
    {
        var lines = new List<string>();
        if (a.Phase != b.Phase)
        {
            lines.Add($"phase {a.Phase} -> {b.Phase}");
        }
        if (a.Act != b.Act && b.Act != null)
        {
            lines.Add($"act {a.Act} -> {b.Act}");
        }
        if (a.Floor != b.Floor && b.Floor != null)
        {
            lines.Add($"floor {a.Floor} -> {b.Floor}");
        }
        if (a.Turn != b.Turn && b.Turn != null)
        {
            lines.Add($"turn {I(b.Turn.Value)}");
        }
        if (a.Hp != b.Hp || a.MaxHp != b.MaxHp)
        {
            lines.Add($"hp {I(a.Hp)}/{I(a.MaxHp)} -> {I(b.Hp)}/{I(b.MaxHp)}");
        }
        if (a.Block != b.Block)
        {
            lines.Add($"block {I(a.Block)} -> {I(b.Block)}");
        }
        if (a.Gold != b.Gold)
        {
            lines.Add($"gold {I(a.Gold)} -> {I(b.Gold)}");
        }
        foreach (var (index, now) in b.Enemies)
        {
            if (a.Enemies.TryGetValue(index, out var was))
            {
                if (was.Hp != now.Hp)
                {
                    lines.Add(now.Hp <= 0 && was.Hp > 0
                        ? $"enemy {I(index)} {now.Id} died"
                        : $"enemy {I(index)} {now.Id} hp {I(was.Hp)} -> {I(now.Hp)}");
                }
                if (was.Block != now.Block)
                {
                    lines.Add($"enemy {I(index)} {now.Id} block {I(was.Block)} -> {I(now.Block)}");
                }
            }
            else
            {
                lines.Add($"enemy {I(index)} {now.Id} appeared ({I(now.Hp)} hp)");
            }
        }
        AddMultisetDiff(lines, "deck", a.Deck, b.Deck);
        AddMultisetDiff(lines, "relic", a.Relics, b.Relics);
        AddMultisetDiff(lines, "potion", a.Potions.Where(p => p != null).Select(p => p!).ToList(), b.Potions.Where(p => p != null).Select(p => p!).ToList());
        return lines;
    }

    private static void AddMultisetDiff(List<string> lines, string label, List<string> before, List<string> after)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var x in before)
        {
            counts[x] = counts.GetValueOrDefault(x) - 1;
        }
        foreach (var x in after)
        {
            counts[x] = counts.GetValueOrDefault(x) + 1;
        }
        var added = new List<string>();
        var removed = new List<string>();
        foreach (var (id, n) in counts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            for (int i = 0; i < n; i++)
            {
                added.Add(id);
            }
            for (int i = 0; i < -n; i++)
            {
                removed.Add(id);
            }
        }
        if (added.Count > 0)
        {
            lines.Add($"{label} + {string.Join(", ", added)}");
        }
        if (removed.Count > 0)
        {
            lines.Add($"{label} - {string.Join(", ", removed)}");
        }
    }
}
