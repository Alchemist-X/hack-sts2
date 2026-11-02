namespace Sts2Sim.Diff;

// A projection rule with no port yet. The shared player-view helpers (Core PlayerView, D9) throw the base type;
// the runners catch PlayerViewGapException, which covers both.
public sealed class ProjectionGapException : PlayerViewGapException
{
    public ProjectionGapException(string message)
        : base(message)
    {
    }
}

public static class SimProjector
{
    public const string StatusCardCountKey = "card_count";

    public static IReadOnlySet<string> PowersWithDisplayAmountOverride => PlayerView.PowersWithDisplayAmountOverride;

    public static IReadOnlySet<string> PowersWithVisibilityOverride => PlayerView.PowersWithVisibilityOverride;

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
    {
        var cost = PlayerView.CostOf(cs, card);
        return cost.IsX ? CardCost.X : CardCost.Of(cost.Value);
    }

    public static bool CanPlay(CombatState cs, CardInstance card) => PlayerView.CanPlay(cs, card);

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

    public static bool IsVisible(CombatState cs, PowerModel power) => PlayerView.IsVisible(cs, power);

    public static int DisplayAmount(PowerModel power) => PlayerView.DisplayAmount(power);

    public static IntentProjection Intent(CombatState cs, MonsterState owner, AbstractIntent intent, IReadOnlyList<CreatureState> targets)
    {
        var view = PlayerView.Intent(cs, owner, intent, targets);
        string type = view.Type.ToString();
        switch (intent)
        {
            case AttackIntent:
                return new IntentProjection
                {
                    Type = type,
                    AttackValue = view.Damage,
                    AttackTimes = view.Times
                };
            case StatusIntent:
                return new IntentProjection
                {
                    Type = type,
                    Extras = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal)
                    {
                        [StatusCardCountKey] = JsonValue.Create(view.CardCount!.Value)
                    }
                };
            default:
                return new IntentProjection { Type = type };
        }
    }
}
