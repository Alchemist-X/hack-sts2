namespace Sts2Sim.Play;

public readonly record struct CreatureSnapshot(int CombatId, string Tag, int Hp, int MaxHp);

public static class StateRenderer
{
    public const string PlayerTag = "you";

    public const string TerminalMarker = "TERMINAL";

    private static readonly CardKeyword[] ShownKeywords =
    {
        CardKeyword.Innate, CardKeyword.Retain, CardKeyword.Sly, CardKeyword.Ethereal,
        CardKeyword.Exhaust, CardKeyword.Unplayable, CardKeyword.Eternal
    };

    public static string Render(PlayGame game)
    {
        var sb = new StringBuilder(2048);
        AppendState(sb, game);
        return sb.ToString();
    }

    public static void AppendState(StringBuilder sb, PlayGame game)
    {
        var cs = game.Sim.State;
        var session = game.Session;
        string phase = game.IsOver ? "over" : cs.Player.Phase.ToString();
        Line(sb, $"== COMBAT seed {session.Seed} | {session.EncounterId} | A{session.Ascension} | turn {cs.Player.TurnNumber} | phase {phase} | actions {session.Actions.Count}/{session.ActionCap} | invalid {session.InvalidAttempts}");
        if (game.IsOver)
        {
            AppendTerminal(sb, game);
            return;
        }
        var player = cs.Player;
        Line(sb, $"PLAYER hp {player.CurrentHp}/{player.MaxHp} | block {player.Block} | energy {player.Energy}/{EnergyCmd.GetMaxEnergy(cs, player)} | powers: {Powers(player)}");
        AppendHand(sb, cs);
        Line(sb, $"PILES draw {player.DrawPile.Count} | discard {player.DiscardPile.Count} | exhaust {player.ExhaustPile.Count}");
        AppendEnemies(sb, cs);
        AppendLegalActions(sb, cs, game.Sim.LegalActions());
    }

    public static string ActionLabel(CombatState cs, GameAction action, bool nameTarget)
    {
        if (action.Kind == ActionKind.EndTurn)
        {
            return "End Turn";
        }
        var card = cs.Player.Hand[action.CardIndex];
        string label = "Play " + CardName(card);
        if (nameTarget && action.TargetIndex != GameAction.NoIndex)
        {
            label += " -> " + MonsterTag(cs.Monsters[action.TargetIndex]);
        }
        return label;
    }

    public static string ActionText(CombatState cs, GameAction action)
    {
        string label = ActionLabel(cs, action, cs.Monsters.Count > 1);
        if (action.Kind == ActionKind.EndTurn)
        {
            return label;
        }
        var card = cs.Player.Hand[action.CardIndex];
        CreatureState? target = action.TargetIndex == GameAction.NoIndex ? null : cs.Monsters[action.TargetIndex];
        return $"{label} ({Effect(cs, card, target)})";
    }

    public static string CardName(CardInstance card)
    {
        string name = card.Id switch
        {
            StrikeIronclad.CardId => "Strike",
            DefendIronclad.CardId => "Defend",
            AscendersBane.CardId => "Ascender's Bane",
            _ => TitleCase(card.Id)
        };
        return card.IsUpgraded ? name + "+" : name;
    }

    public static string MonsterTag(MonsterState monster) => $"E{monster.CombatId} {TitleCase(monster.MonsterId)}";

    public static string Effect(CombatState cs, CardInstance card, CreatureState? target)
    {
        var parts = new List<string>(4);
        var vars = card.Vars.All;
        for (int i = 0; i < vars.Count; i++)
        {
            var v = vars[i];
            switch (v.Name)
            {
                case DynamicVarSet.DamageName:
                    parts.Add($"{PreviewDamage(cs, card, v, target)} dmg");
                    break;
                case DynamicVarSet.BlockName:
                    parts.Add($"{PreviewBlock(cs, card, v)} block");
                    break;
                case DynamicVarSet.CardsName:
                    parts.Add($"draw {v.IntValue}");
                    break;
                case DynamicVarSet.EnergyName:
                    parts.Add($"gain {v.IntValue} energy");
                    break;
                case DynamicVarSet.RepeatName:
                    parts.Add($"{v.IntValue} times");
                    break;
                default:
                    parts.Add(v.Name.Length > 5 && v.Name.EndsWith("Power", StringComparison.Ordinal)
                        ? $"{v.IntValue} {SplitCamel(v.Name[..^5])}"
                        : $"{SplitCamel(v.Name)} {v.IntValue}");
                    break;
            }
        }
        foreach (var keyword in ShownKeywords)
        {
            if (card.HasKeyword(keyword))
            {
                parts.Add(keyword.ToString());
            }
        }
        return parts.Count == 0 ? "no effect" : string.Join("; ", parts);
    }

    public static string Intent(CombatState cs, MonsterState monster)
    {
        if (!monster.HasAi || monster.Ai.NextMove is not { } move)
        {
            return "none";
        }
        var targets = MonsterLifecycle.PlayerCreatures(cs);
        var parts = new List<string>(2);
        foreach (var intent in move.Intents)
        {
            switch (intent)
            {
                case AttackIntent attack:
                {
                    int single = attack.GetSingleDamage(cs, targets, monster);
                    int repeats = attack.GetRepeats(cs, monster);
                    string kind = attack.IntentType == IntentType.DeathBlow ? "Death Blow" : "Attack";
                    parts.Add(repeats == 1 ? $"{kind} {single}" : $"{kind} {single}x{repeats} (={single * repeats})");
                    break;
                }
                case StatusIntent status:
                    parts.Add($"Status (adds {status.CardCount} status card{(status.CardCount == 1 ? string.Empty : "s")})");
                    break;
                default:
                    parts.Add(IntentName(intent.IntentType));
                    break;
            }
        }
        return parts.Count == 0 ? "none" : string.Join(" + ", parts);
    }

    public static string Powers(CreatureState creature)
    {
        var powers = creature.Powers;
        if (powers.Count == 0)
        {
            return "none";
        }
        var parts = new string[powers.Count];
        for (int i = 0; i < powers.Count; i++)
        {
            parts[i] = PowerText(powers[i]);
        }
        return string.Join(", ", parts);
    }

    public static List<CreatureSnapshot> Snapshot(CombatState cs)
    {
        var player = cs.Player;
        var list = new List<CreatureSnapshot>(1 + cs.Monsters.Count)
        {
            new(player.CombatId, PlayerTag, player.CurrentHp, player.MaxHp)
        };
        foreach (var monster in cs.Monsters)
        {
            list.Add(new CreatureSnapshot(monster.CombatId, MonsterTag(monster), monster.CurrentHp, monster.MaxHp));
        }
        return list;
    }

    public static string Changes(IReadOnlyList<CreatureSnapshot> before, IReadOnlyList<CreatureSnapshot> after)
    {
        var parts = new List<string>(4);
        foreach (var b in before)
        {
            int index = IndexOf(after, b.CombatId);
            if (index < 0)
            {
                parts.Add($"{b.Tag} hp {b.Hp}->0 (died)");
                continue;
            }
            var a = after[index];
            if (a.Hp != b.Hp)
            {
                parts.Add(a.Hp == 0 ? $"{b.Tag} hp {b.Hp}->0 (died)" : $"{b.Tag} hp {b.Hp}->{a.Hp}");
            }
        }
        foreach (var a in after)
        {
            if (IndexOf(before, a.CombatId) < 0)
            {
                parts.Add($"{a.Tag} appeared (hp {a.Hp}/{a.MaxHp})");
            }
        }
        return parts.Count == 0 ? "none" : string.Join(" | ", parts);
    }

    private static void AppendHand(StringBuilder sb, CombatState cs)
    {
        var hand = cs.Player.Hand.Cards;
        Line(sb, $"HAND {hand.Count} cards (hand order; NOT action numbers):");
        for (int i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            string playability = card.CanPlay(cs, out var reason) ? "playable" : "unplayable: " + Reasons(reason);
            Line(sb, $"  - {CardName(card)} | {card.Type} | cost {Cost(cs, card)} | upgraded {(card.IsUpgraded ? "yes" : "no")} | {playability} | {Effect(cs, card, null)}");
        }
    }

    private static void AppendEnemies(StringBuilder sb, CombatState cs)
    {
        var monsters = cs.Monsters;
        Line(sb, $"ENEMIES {monsters.Count}:");
        foreach (var monster in monsters)
        {
            string dead = monster.IsDead ? " (dead)" : string.Empty;
            Line(sb, $"  {MonsterTag(monster)} | hp {monster.CurrentHp}/{monster.MaxHp}{dead} | block {monster.Block} | intent: {Intent(cs, monster)} | powers: {Powers(monster)}");
        }
    }

    private static void AppendLegalActions(StringBuilder sb, CombatState cs, IReadOnlyList<GameAction> legal)
    {
        Line(sb, $"LEGAL ACTIONS {legal.Count} (reply with exactly one number):");
        for (int i = 0; i < legal.Count; i++)
        {
            Line(sb, $"  {i + 1}. {ActionText(cs, legal[i])}");
        }
    }

    private static void AppendTerminal(StringBuilder sb, PlayGame game)
    {
        var cs = game.Sim.State;
        var player = cs.Player;
        Line(sb, $"PLAYER hp {player.CurrentHp}/{player.MaxHp}");
        var alive = new List<string>(cs.Monsters.Count);
        foreach (var monster in cs.Monsters)
        {
            if (monster.IsAlive)
            {
                alive.Add($"{MonsterTag(monster)} hp {monster.CurrentHp}/{monster.MaxHp}");
            }
        }
        Line(sb, "ENEMIES left: " + (alive.Count == 0 ? "none" : string.Join(" | ", alive)));
        Line(sb, TerminalMarker);
        Line(sb, game.Stats().ToJsonLine());
    }

    private static string Cost(CombatState cs, CardInstance card)
    {
        var cost = card.EnergyCost;
        if (cost.CostsX)
        {
            return "X";
        }
        if (cost.Base < 0)
        {
            return "-";
        }
        return cost.GetWithModifiers(cs, card, CostModifiers.All).ToString(CultureInfo.InvariantCulture);
    }

    private static int PreviewDamage(CombatState cs, CardInstance card, DynamicVar v, CreatureState? target)
        => (int)Math.Max(0m, Hook.ModifyDamage(cs, target, cs.Player, v.BaseValue, v.Props, card, null));

    private static int PreviewBlock(CombatState cs, CardInstance card, DynamicVar v)
        => (int)Math.Max(0m, Hook.ModifyBlock(cs, cs.Player, v.BaseValue, v.Props, card, null));

    private static string Reasons(UnplayableReason reason)
    {
        var parts = new List<string>(2);
        if ((reason & UnplayableReason.HasUnplayableKeyword) != 0)
        {
            parts.Add("Unplayable card");
        }
        if ((reason & UnplayableReason.EnergyCostTooHigh) != 0)
        {
            parts.Add("not enough energy");
        }
        if ((reason & UnplayableReason.StarCostTooHigh) != 0)
        {
            parts.Add("not enough stars");
        }
        if ((reason & UnplayableReason.BlockedByHook) != 0)
        {
            parts.Add("blocked by an effect");
        }
        if ((reason & UnplayableReason.BlockedByCardLogic) != 0)
        {
            parts.Add("play condition not met");
        }
        if ((reason & UnplayableReason.NoLivingAllies) != 0)
        {
            parts.Add("no living ally");
        }
        return parts.Count == 0 ? "unknown" : string.Join(", ", parts);
    }

    private static string PowerText(PowerModel power)
    {
        string name = TitleCase(power.Id.EndsWith("_POWER", StringComparison.Ordinal) ? power.Id[..^6] : power.Id);
        return power switch
        {
            ShrinkPower { IsInfinite: true } => $"{name} permanent (deals {ShrinkPower.DamageDecrease}% less attack dmg)",
            ShrinkPower => $"{name} {power.Amount} (deals {ShrinkPower.DamageDecrease}% less attack dmg)",
            RavenousPower => $"{name} {power.Amount} (when another enemy dies: stunned for its next move, gains {power.Amount} Strength)",
            _ => $"{name} {power.Amount}"
        };
    }

    private static string IntentName(IntentType type) => type switch
    {
        IntentType.DebuffStrong => "Strong Debuff",
        IntentType.CardDebuff => "Card Debuff",
        IntentType.Stun => "Stunned",
        IntentType.DeathBlow => "Death Blow",
        IntentType.StatusCard => "Status",
        _ => type.ToString()
    };

    private static string TitleCase(string id)
    {
        var words = id.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i];
            words[i] = w.Length <= 1 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant();
        }
        return string.Join(' ', words);
    }

    private static string SplitCamel(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
            {
                sb.Append(' ');
            }
            sb.Append(name[i]);
        }
        return sb.ToString();
    }

    private static int IndexOf(IReadOnlyList<CreatureSnapshot> list, int combatId)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].CombatId == combatId)
            {
                return i;
            }
        }
        return -1;
    }

    private static void Line(StringBuilder sb, string text) => sb.Append(text).Append('\n');
}
