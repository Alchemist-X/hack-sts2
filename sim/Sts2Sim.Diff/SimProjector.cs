namespace Sts2Sim.Diff;

public sealed class ProjectionGapException : Exception
{
    public ProjectionGapException(string message)
        : base(message)
    {
    }
}

public static class SimProjector
{
    public const string StatusCardCountKey = "card_count";

    public static readonly IReadOnlySet<string> PowersWithDisplayAmountOverride = new HashSet<string>(StringComparer.Ordinal)
    {
        "AUTOMATION_POWER", "CACOPHONY_POWER", "FERAL_POWER", "HARDENED_SHELL_POWER", "MONOLOGUE_POWER",
        "ORBIT_POWER", "PALE_BLUE_DOT_POWER", "PANACHE_POWER", "SLOTH_POWER", "SLOW_POWER", "TAG_TEAM_POWER",
        "TENDER_POWER", "WITHERING_PRESENCE_POWER"
    };

    public static readonly IReadOnlySet<string> PowersWithVisibilityOverride = new HashSet<string>(StringComparer.Ordinal)
    {
        "AMBERGRIS_POWER"
    };

    public static Projection Project(CombatState cs)
    {
        ArgumentNullException.ThrowIfNull(cs);
        var player = cs.Player;
        var handCards = player.Hand.Cards;
        var hand = new List<HandCardProjection>(handCards.Count);
        for (int i = 0; i < handCards.Count; i++)
        {
            var card = handCards[i];
            hand.Add(new HandCardProjection(card.Id, card.IsUpgraded, CostOf(cs, card), CanPlay(cs, card)));
        }
        var drawMultiset = PileKeys(player.DrawPile);
        drawMultiset.Sort(StringComparer.Ordinal);
        var playerProjection = new PlayerProjection
        {
            Hp = player.CurrentHp,
            MaxHp = player.MaxHp,
            Block = player.Block,
            Energy = player.Energy,
            MaxEnergy = EnergyCmd.GetMaxEnergy(cs, player),
            Hand = hand,
            DrawCount = player.DrawPile.Count,
            DiscardCount = player.DiscardPile.Count,
            ExhaustCount = player.ExhaustPile.Count,
            DrawMultiset = drawMultiset,
            Discard = PileKeys(player.DiscardPile),
            Exhaust = PileKeys(player.ExhaustPile),
            Powers = Powers(cs, player)
        };

        var alive = new List<MonsterState>(cs.Monsters.Count);
        foreach (var m in cs.Monsters)
        {
            if (m.IsEnemy && m.IsAlive)
            {
                alive.Add(m);
            }
        }
        alive.Sort(static (a, b) => a.CombatId.CompareTo(b.CombatId));
        var targets = MonsterLifecycle.PlayerCreatures(cs);
        var enemies = new List<EnemyProjection>(alive.Count);
        foreach (var m in alive)
        {
            var intents = new List<IntentProjection>();
            var move = m.HasAi ? m.Ai.NextMove : null;
            if (move != null)
            {
                foreach (var intent in move.Intents)
                {
                    intents.Add(Intent(cs, m, intent, targets));
                }
            }
            enemies.Add(new EnemyProjection
            {
                CombatId = m.CombatId,
                MonsterId = m.MonsterId,
                Hp = m.CurrentHp,
                MaxHp = m.MaxHp,
                Block = m.Block,
                Powers = Powers(cs, m),
                Intents = intents
            });
        }

        return new Projection
        {
            Turn = new TurnProjection(player.TurnNumber, Phase(cs)),
            Player = playerProjection,
            Enemies = enemies
        };
    }

    public static string Phase(CombatState cs)
    {
        if (cs.CurrentSide != CombatSide.Player)
        {
            return DeterministicHash.SnakeCase(cs.CurrentSide.ToString());
        }
        var phase = cs.Player.Phase;
        return phase == PlayerTurnPhase.Play
            ? ProjectionJson.PhasePlayerPlay
            : "player_" + DeterministicHash.SnakeCase(phase.ToString());
    }

    public static CardCost CostOf(CombatState cs, CardInstance card)
        => card.EnergyCost.CostsX ? CardCost.X : CardCost.Of(card.EnergyCost.GetAmountToSpend(cs, card));

    public static bool CanPlay(CombatState cs, CardInstance card)
        => card.CanPlay(cs, out var reason) && reason == UnplayableReason.None;

    public static string CardKey(CardInstance card) => ProjectionJson.CardKey(card.Id, card.IsUpgraded);

    public static List<string> PileKeys(CardPile pile)
    {
        var cards = pile.Cards;
        var keys = new List<string>(cards.Count);
        for (int i = 0; i < cards.Count; i++)
        {
            keys.Add(CardKey(cards[i]));
        }
        return keys;
    }

    public static IReadOnlyList<PowerProjection> Powers(CombatState cs, CreatureState creature)
    {
        var powers = creature.Powers;
        if (powers.Count == 0)
        {
            return Array.Empty<PowerProjection>();
        }
        var list = new List<PowerProjection>(powers.Count);
        for (int i = 0; i < powers.Count; i++)
        {
            var power = powers[i];
            if (IsVisible(cs, power))
            {
                list.Add(new PowerProjection(power.Id, DisplayAmount(power)));
            }
        }
        return list;
    }

    public static bool IsVisible(CombatState cs, PowerModel power)
    {
        if (PowersWithVisibilityOverride.Contains(power.Id))
        {
            throw new ProjectionGapException($"{power.Id} overrides PowerModel.IsVisibleInternal in the game; the diff projection has no rule for it yet");
        }
        int targetId = power.TargetId;
        if (targetId == PowerModel.NoCreature || targetId == cs.Player.CombatId)
        {
            return true;
        }
        var target = cs.GetCreatureById(targetId);
        return target == null || target.IsEnemy;
    }

    public static int DisplayAmount(PowerModel power)
    {
        if (PowersWithDisplayAmountOverride.Contains(power.Id))
        {
            throw new ProjectionGapException($"{power.Id} overrides PowerModel.DisplayAmount in the game; the diff projection has no rule for it yet");
        }
        return power.Amount;
    }

    public static IntentProjection Intent(CombatState cs, MonsterState owner, AbstractIntent intent, IReadOnlyList<CreatureState> targets)
    {
        string type = intent.IntentType.ToString();
        switch (intent)
        {
            case AttackIntent attack:
                return new IntentProjection
                {
                    Type = type,
                    AttackValue = attack.GetSingleDamage(cs, targets, owner),
                    AttackTimes = attack.GetRepeats(cs, owner)
                };
            case StatusIntent status:
                return new IntentProjection
                {
                    Type = type,
                    Extras = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal)
                    {
                        [StatusCardCountKey] = JsonValue.Create(status.CardCount)
                    }
                };
            default:
                return new IntentProjection { Type = type };
        }
    }
}
