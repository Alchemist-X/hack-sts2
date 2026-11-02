using System.Globalization;

namespace Sts2Sim.Server;

// Step log v2 (sim/PLAY.md "Step log"): a before/after snapshot keyed by creature uid (the kernel's CombatId, stable
// for the whole combat) plus a narration of the kernel's own CombatHistory entries appended during the step
// (State/CombatHistory.cs, a port of the game's CombatHistory: card plays, potions, damage, block, powers, monster
// moves, draws, exhausts). Read-only: nothing here touches the simulation.
public static class StepLog
{
    // NextMove: the move the creature intends (its intent as shown before the step), null without an AI.
    public sealed record Creature(int CombatId, string Id, bool Enemy, int Hp, int MaxHp, int Block, bool Alive, SortedDictionary<string, int> Powers, CreatureState Ref, MoveStateDef? NextMove = null);

    public sealed record Snapshot(
        string Phase,
        int? Act,
        int? Floor,
        int? Turn,
        int Hp,
        int MaxHp,
        int Block,
        int? Energy,
        int Gold,
        int Stars,
        List<string> Deck,
        List<string> Relics,
        List<string?> Potions,
        CombatState? Combat,
        bool CombatInProgress,
        int HistoryCount,
        int PlayerCombatId,
        SortedDictionary<string, int>? PlayerPowers,
        Dictionary<int, Creature> Creatures);

    private static CombatState? RawCombat(GameSession g) => g.Combat != null ? g.Combat.State : g.Run!.State.Combat;

    // fallback: the before-step combat, used when the run dropped its combat at combat end (the object was mutated in place).
    public static Snapshot Capture(GameSession g, CombatState? fallback = null)
    {
        var p = g.Player;
        var rs = g.Run?.State;
        var cs = RawCombat(g) ?? fallback;
        var creatures = new Dictionary<int, Creature>();
        SortedDictionary<string, int>? playerPowers = null;
        if (cs != null)
        {
            foreach (var m in cs.Monsters)
            {
                creatures[m.CombatId] = Snap(cs, m, true);
            }
            for (int a = 1; a < cs.Allies.Count; a++)
            {
                if (cs.Allies[a] is MonsterState pet)
                {
                    creatures[pet.CombatId] = Snap(cs, pet, false);
                }
            }
            playerPowers = PowerMap(cs, cs.Player);
        }
        bool inProgress = cs is { IsInProgress: true };
        return new Snapshot(
            g.PhaseName,
            rs != null ? rs.CurrentActIndex + 1 : null,
            rs?.TotalFloor,
            inProgress ? cs!.Player.TurnNumber : null,
            p.CurrentHp,
            p.MaxHp,
            p.Block,
            inProgress ? p.Energy : null,
            p.Gold,
            p.Stars,
            p.Deck.Select(Describe.CardName).ToList(),
            p.Relics.Select(r => r.Id).ToList(),
            p.PotionSlots.Select(s => s?.Id).ToList(),
            cs,
            inProgress,
            cs?.History.Count ?? 0,
            cs?.Player.CombatId ?? -1,
            playerPowers,
            creatures);
    }

    private static Creature Snap(CombatState cs, MonsterState m, bool enemy)
        => new(m.CombatId, m.MonsterId, enemy, m.CurrentHp, m.MaxHp, m.Block, m.IsAlive, PowerMap(cs, m), m, m.HasAi ? m.Ai.NextMove : null);

    private static SortedDictionary<string, int> PowerMap(CombatState cs, CreatureState c)
    {
        var map = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, amount) in Describe.Powers(cs, c))
        {
            map[id] = map.GetValueOrDefault(id) + amount;
        }
        return map;
    }

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    // The step's history entries: those appended to the after-step combat since the before snapshot. A different
    // combat object is either a fresh combat (whole history) or the kernel's replay of a prompted action from its
    // pre-action clone (same encounter, history at least as long), whose sequence numbers continue the same history.
    private static (CombatState? Cs, int Start, bool NewCombat) Window(Snapshot a, Snapshot b)
    {
        var cs = b.Combat;
        if (cs == null)
        {
            return (null, 0, false);
        }
        if (a.Combat == null || (!ReferenceEquals(a.Combat, cs) && (!a.CombatInProgress || a.Combat.EncounterId != cs.EncounterId || cs.History.Count < a.HistoryCount)))
        {
            return (cs, 0, true);
        }
        return (cs, a.HistoryCount, false);
    }

    // revealMoves: name each enemy move in the enemy-turn narration (the game's "reveal_moves" option); off, a move is
    // told by its effects only, as the game shows it.
    public static List<string> Diff(Snapshot a, Snapshot b, RunAction? action = null, bool revealMoves = false)
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
        var (cs, start, newCombat) = Window(a, b);
        var entries = new List<IHistoryEntry>();
        if (cs != null)
        {
            var all = new List<IHistoryEntry>();
            cs.History.GetEntriesInOrder(all);
            entries.AddRange(all.Where(e => e.Stamp.Sequence >= start));
        }
        var names = new Dictionary<int, string>();
        foreach (var c in a.Creatures.Values.Concat(b.Creatures.Values))
        {
            names[c.CombatId] = "u" + I(c.CombatId) + " " + c.Id;
        }
        string Label(int id)
        {
            if (id == b.PlayerCombatId || id == a.PlayerCombatId)
            {
                return "player";
            }
            if (names.TryGetValue(id, out var n))
            {
                return n;
            }
            if (cs?.GetCreatureById(id) is MonsterState m)
            {
                return "u" + I(id) + " " + m.MonsterId;
            }
            return "u" + I(id);
        }
        if (newCombat && cs != null)
        {
            lines.Add("combat start: " + string.Join(", ", b.Creatures.Values.Where(c => c.Enemy && c.Alive).OrderBy(c => c.CombatId).Select(c => $"u{I(c.CombatId)} {c.Id} {I(c.Hp)}/{I(c.MaxHp)}")));
        }
        var existed = new HashSet<int>(a.Creatures.Keys) { a.PlayerCombatId, b.PlayerCombatId };
        // The intent types of a performed move: the move as intended before the step (also moves made at run time,
        // REVIVE_MOVE, STUNNED), else the monster's move table.
        string? IntentOf(int id, string moveId)
        {
            if (a.Creatures.TryGetValue(id, out var was) && was.NextMove is { } intended && intended.Id == moveId)
            {
                return IntentTypes(intended);
            }
            var creature = b.Creatures.TryGetValue(id, out var now) ? now.Ref : was?.Ref ?? cs?.GetCreatureById(id);
            return MoveIntentText(creature, moveId);
        }
        Narrate(lines, entries, action, Label, newCombat ? null : existed, revealMoves, IntentOf);
        if (a.Turn != b.Turn && b.Turn != null)
        {
            lines.Add($"turn {I(b.Turn.Value)}");
        }
        if (a.Hp != b.Hp || a.MaxHp != b.MaxHp)
        {
            lines.Add($"hp {I(a.Hp)}/{I(a.MaxHp)} -> {I(b.Hp)}/{I(b.MaxHp)}" + DamageNote(entries, b.PlayerCombatId, b.Hp - a.Hp));
        }
        if (a.Block != b.Block)
        {
            lines.Add($"block {I(a.Block)} -> {I(b.Block)}");
        }
        if (a.Energy != null && b.Energy != null && a.Energy != b.Energy)
        {
            lines.Add($"energy {I(a.Energy.Value)} -> {I(b.Energy.Value)}");
        }
        if (a.Stars != b.Stars)
        {
            lines.Add($"stars {I(a.Stars)} -> {I(b.Stars)}");
        }
        if (a.Gold != b.Gold)
        {
            lines.Add($"gold {I(a.Gold)} -> {I(b.Gold)}");
        }
        if (!newCombat && a.PlayerPowers != null && b.PlayerPowers != null)
        {
            PowerDiff(lines, "player", a.PlayerPowers, b.PlayerPowers);
        }
        if (!newCombat && cs != null)
        {
            CreatureDiff(lines, a, b, entries, cs);
        }
        CardFlow(lines, entries);
        AddMultisetDiff(lines, "deck", a.Deck, b.Deck);
        AddMultisetDiff(lines, "relic", a.Relics, b.Relics);
        AddMultisetDiff(lines, "potion", a.Potions.Where(p => p != null).Select(p => p!).ToList(), b.Potions.Where(p => p != null).Select(p => p!).ToList());
        return lines;
    }

    // " (6 dmg, 0 blocked)" from the step's DamageReceived entries for a creature, or " (healed N)".
    private static string DamageNote(List<IHistoryEntry> entries, int combatId, int hpDelta)
    {
        int dmg = 0;
        int blocked = 0;
        bool any = false;
        foreach (var e in entries)
        {
            if (e is DamageReceivedEntry d && d.ReceiverId == combatId)
            {
                any = true;
                dmg += d.Result.UnblockedDamage;
                blocked += d.Result.BlockedDamage;
            }
        }
        if (any)
        {
            return $" ({I(dmg)} dmg, {I(blocked)} blocked)";
        }
        return hpDelta > 0 ? $" (healed {I(hpDelta)})" : string.Empty;
    }

    private static void CreatureDiff(List<string> lines, Snapshot a, Snapshot b, List<IHistoryEntry> entries, CombatState cs)
    {
        var ids = new SortedSet<int>(a.Creatures.Keys.Concat(b.Creatures.Keys));
        foreach (int id in ids)
        {
            bool had = a.Creatures.TryGetValue(id, out var was);
            bool has = b.Creatures.TryGetValue(id, out var now);
            if (!had)
            {
                if (now!.Alive)
                {
                    lines.Add($"u{I(id)} {now.Id} spawned ({I(now.Hp)}/{I(now.MaxHp)} hp{(now.Enemy ? string.Empty : ", ally")})");
                }
                continue;
            }
            string label = "u" + I(id) + " " + was!.Id;
            if (!has)
            {
                // Removed from the combat lists: died (normal enemies are removed on death) or escaped.
                var r = was.Ref;
                if (r.CurrentHp != was.Hp)
                {
                    lines.Add($"{label} hp {I(was.Hp)} -> {I(r.CurrentHp)}" + DamageNote(entries, id, r.CurrentHp - was.Hp));
                }
                if (was.Alive)
                {
                    lines.Add(cs.EscapedCreatures.Any(c => c.CombatId == id) ? $"{label} escaped" : $"{label} died");
                }
                continue;
            }
            if (was.Hp != now!.Hp || was.MaxHp != now.MaxHp)
            {
                string hp = was.MaxHp != now.MaxHp ? $"{I(was.Hp)}/{I(was.MaxHp)} -> {I(now.Hp)}/{I(now.MaxHp)}" : $"{I(was.Hp)} -> {I(now.Hp)}";
                lines.Add($"{label} hp {hp}" + DamageNote(entries, id, now.Hp - was.Hp));
            }
            if (was.Alive && !now.Alive)
            {
                lines.Add($"{label} died");
                continue;
            }
            if (!was.Alive && now.Alive)
            {
                lines.Add($"{label} revived");
            }
            if (was.Block != now.Block)
            {
                lines.Add($"{label} block {I(was.Block)} -> {I(now.Block)}");
            }
            PowerDiff(lines, "u" + I(id), was.Powers, now.Powers);
        }
    }

    // "u2 +VULNERABLE_POWER 2", "u2 -VULNERABLE_POWER", "player STRENGTH_POWER 2 -> 4".
    private static void PowerDiff(List<string> lines, string who, SortedDictionary<string, int> before, SortedDictionary<string, int> after)
    {
        foreach (var (id, amount) in after)
        {
            if (!before.TryGetValue(id, out int old))
            {
                lines.Add($"{who} +{id} {I(amount)}");
            }
            else if (old != amount)
            {
                lines.Add($"{who} {id} {I(old)} -> {I(amount)}");
            }
        }
        foreach (var id in before.Keys)
        {
            if (!after.ContainsKey(id))
            {
                lines.Add($"{who} -{id}");
            }
        }
    }

    private static void CardFlow(List<string> lines, List<IHistoryEntry> entries)
    {
        int drawn = entries.Count(e => e is CardDrawnEntry);
        var discarded = entries.OfType<CardDiscardedEntry>().Select(e => e.CardId).ToList();
        var exhausted = entries.OfType<CardExhaustedEntry>().Select(e => e.CardId).ToList();
        var generated = entries.OfType<CardGeneratedEntry>().Select(e => e.CardId).ToList();
        var parts = new List<string>();
        if (drawn > 0)
        {
            parts.Add("drew " + I(drawn));
        }
        if (discarded.Count > 0)
        {
            parts.Add("discarded " + Counted(discarded));
        }
        if (exhausted.Count > 0)
        {
            parts.Add("exhausted " + Counted(exhausted));
        }
        if (generated.Count > 0)
        {
            parts.Add("generated " + Counted(generated));
        }
        if (parts.Count > 0)
        {
            lines.Add("cards: " + string.Join("; ", parts));
        }
    }

    // "A x2, B" in first-seen order.
    private static string Counted(List<string> ids)
    {
        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!counts.ContainsKey(id))
            {
                order.Add(id);
            }
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }
        return string.Join(", ", order.Select(id => counts[id] > 1 ? $"{id} x{I(counts[id])}" : id));
    }

    // existed: creature ids present before the step (null: a fresh combat); a move whose effects touch a creature not
    // in it is reported as having summoned that creature. revealMoves: lead each move line with the move id (and title).
    private static void Narrate(List<string> lines, List<IHistoryEntry> entries, RunAction? action, Func<int, string> label, HashSet<int>? existed,
        bool revealMoves, Func<int, string, string?> intentOf)
    {
        var reported = new HashSet<int>();
        if (entries.Count == 0)
        {
            return;
        }
        var kind = action?.Kind;
        string Play(CardPlayStartedEntry p)
            => p.CardPlay.CardId + (p.CardPlay.TargetId != CombatHistory.NoId ? " -> " + label(p.CardPlay.TargetId) : string.Empty) + (p.CardPlay.IsAutoPlay ? " (auto)" : string.Empty);
        // Card plays other than the step's own (auto-plays, replays, cards played by potions or effects).
        var plays = entries.OfType<CardPlayStartedEntry>().ToList();
        if (kind == RunActionKind.PlayCard)
        {
            int own = plays.FindIndex(p => !p.CardPlay.IsAutoPlay);
            if (own >= 0)
            {
                plays.RemoveAt(own);
            }
        }
        if (kind == RunActionKind.UsePotion && entries.OfType<PotionUsedEntry>().FirstOrDefault() is { PotionId: not null } potion)
        {
            if (plays.Count > 0)
            {
                lines.Add($"potion {I(action!.Value.A)} {potion.PotionId}: played " + string.Join(", ", plays.Select(Play)));
            }
        }
        else if (plays.Count > 0)
        {
            lines.Add((kind == RunActionKind.PlayCard ? "also played: " : "played: ") + string.Join(", ", plays.Select(Play)));
        }
        bool turnEnd = kind == RunActionKind.EndTurn;
        if (!entries.Any(e => e is MonsterPerformedMoveEntry))
        {
            if (turnEnd)
            {
                StandaloneDamage(lines, entries, label);
            }
            return;
        }
        lines.Add("enemy turn:");
        int segStart = 0;
        for (int k = 0; k < entries.Count; k++)
        {
            if (entries[k] is not MonsterPerformedMoveEntry move)
            {
                continue;
            }
            var seg = entries.GetRange(segStart, k - segStart);
            segStart = k + 1;
            int mover = move.MonsterCombatId;
            bool Attributed(IHistoryEntry e) => e switch
            {
                DamageReceivedEntry d => d.DealerId == mover,
                PowerReceivedEntry p => p.ApplierId == mover,
                BlockGainedEntry b => b.ReceiverId == mover,
                _ => false
            };
            int first = seg.FindIndex(Attributed);
            if (first < 0)
            {
                first = seg.Count;
            }
            StandaloneDamage(lines, seg.GetRange(0, first), label);
            var during = seg.GetRange(first, seg.Count - first);
            var parts = new List<string>();
            if (existed != null)
            {
                foreach (var e in during)
                {
                    int touched = e.Stamp.ActorId;
                    if (touched != CombatHistory.NoId && touched != mover && !existed.Contains(touched) && reported.Add(touched))
                    {
                        parts.Add("summoned " + label(touched));
                    }
                }
            }
            // Hits by receiver, in order: "attack 12 x1 -> player: 7 blocked, 5 dmg".
            var hitOrder = new List<int>();
            var hits = new Dictionary<int, List<HistoryDamageResult>>();
            foreach (var d in during.OfType<DamageReceivedEntry>().Where(d => d.DealerId == mover))
            {
                if (!hits.TryGetValue(d.ReceiverId, out var list))
                {
                    hits[d.ReceiverId] = list = new List<HistoryDamageResult>();
                    hitOrder.Add(d.ReceiverId);
                }
                list.Add(d.Result);
            }
            foreach (int receiver in hitOrder)
            {
                var list = hits[receiver];
                var totals = list.Select(r => r.TotalDamage).ToList();
                string per = totals.Distinct().Count() == 1 ? $"{I(totals[0])} x{I(totals.Count)}" : string.Join("+", totals.Select(I));
                string what = list[0].Props.IsPoweredAttack() ? "attack" : "damage";
                parts.Add($"{what} {per} -> {label(receiver)}: {I(list.Sum(r => r.BlockedDamage))} blocked, {I(list.Sum(r => r.UnblockedDamage))} dmg" + (list.Any(r => r.WasTargetKilled) ? ", killed" : string.Empty));
            }
            // Cards created during the enemy side's turn up to this move (status cards a move shuffles in).
            var generated = seg.OfType<CardGeneratedEntry>().Where(g => g.Stamp.CurrentSide == CombatSide.Enemy).Select(g => g.CardId).ToList();
            foreach (var e in during)
            {
                switch (e)
                {
                    case PowerReceivedEntry p when p.ApplierId == mover:
                        parts.Add($"{(p.Amount >= 0 ? "+" : string.Empty)}{p.PowerId} {((int)p.Amount).ToString(CultureInfo.InvariantCulture)} -> {(p.Stamp.ActorId == mover ? "self" : label(p.Stamp.ActorId))}");
                        break;
                    case PowerReceivedEntry p when p.Amount > 0:
                        parts.Add($"{label(p.Stamp.ActorId)} +{p.PowerId} {I((int)p.Amount)}");
                        break;
                    case BlockGainedEntry b:
                        parts.Add(b.ReceiverId == mover ? $"+{I(b.Amount)} block" : $"{label(b.ReceiverId)} +{I(b.Amount)} block");
                        break;
                    case DamageReceivedEntry d when d.DealerId != mover:
                        parts.Add(DamageText(d, label));
                        break;
                }
            }
            if (generated.Count > 0)
            {
                parts.Add("added " + Counted(generated));
            }
            if (!revealMoves)
            {
                // The game never names the move: its effects, or the intent the player saw for an effect-free move.
                lines.Add($" {label(mover)}: " + (parts.Count > 0 ? string.Join("; ", parts) : intentOf(mover, move.MoveId) ?? "no visible effect"));
                continue;
            }
            string moveName = move.MoveId;
            try
            {
                if (RulesText.EnemyMoveTitle(move.MonsterId, move.MoveId) is { Length: > 0 } title)
                {
                    moveName += " (" + title + ")";
                }
            }
            catch (Exception)
            {
            }
            lines.Add($" {label(mover)}: {moveName}" + (parts.Count > 0 ? " — " + string.Join("; ", parts) : string.Empty));
        }
        StandaloneDamage(lines, entries.GetRange(segStart, entries.Count - segStart), label);
    }

    // "Sleep", "Stun", "Buff + Defend": a move's intent types, what the player saw before it, for a move line without
    // visible effects when the move id stays hidden.
    private static string? IntentTypes(MoveStateDef move)
        => move.Intents.Count > 0 ? string.Join(" + ", move.Intents.Select(i => i.IntentType.ToString())) : null;

    private static string? MoveIntentText(CreatureState? creature, string moveId)
    {
        try
        {
            if (creature is MonsterState { HasAi: true } m && m.Ai.Machine.TryGet(moveId, out var state) && state is MoveStateDef def)
            {
                return IntentTypes(def);
            }
            if (moveId == MoveStateDef.StunnedMoveId)
            {
                return nameof(IntentType.Stun);
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    private static string DamageText(DamageReceivedEntry d, Func<int, string> label)
    {
        string source = d.CardSourceId != null ? $" ({d.CardSourceId})" : d.HasDealer ? $" from {label(d.DealerId)}" : string.Empty;
        return $"{label(d.ReceiverId)} took {I(d.Result.UnblockedDamage)} dmg" + (d.Result.BlockedDamage > 0 ? $", {I(d.Result.BlockedDamage)} blocked" : string.Empty) + source + (d.Result.WasTargetKilled ? ", killed" : string.Empty);
    }

    // Turn-transition damage (end-of-turn cards and powers, poison, ...) outside a monster's move.
    private static void StandaloneDamage(List<string> lines, List<IHistoryEntry> entries, Func<int, string> label)
    {
        foreach (var e in entries)
        {
            if (e is DamageReceivedEntry d)
            {
                lines.Add(" " + DamageText(d, label));
            }
        }
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
