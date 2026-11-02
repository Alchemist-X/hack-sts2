namespace Sts2Sim.Core.Powers;

public sealed class VitalSparkPower : PowerModel
{
    public const string PowerId = "VITAL_SPARK_POWER";

    public const string TaintedAfflictionId = "TAINTED";

    public const string TaintedPowerId = "TAINTED_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeCombatStart(RunState rs)
    {
        var cs = rs.Combat!;
        var allies = new List<CreatureState>(cs.Allies);
        for (int a = 0; a < allies.Count; a++)
        {
            if (allies[a] is not PlayerState player)
            {
                continue;
            }
            var cards = AllCards(player);
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Type == CardType.Skill)
                {
                    CardCmd.Afflict(cs, TaintedAfflictionId, cards[i], Amount);
                }
            }
        }
    }

    public override void AfterCardEnteredCombat(CombatState cs, CardInstance card)
    {
        if (card.Affliction == null && card.Type == CardType.Skill)
        {
            CardCmd.Afflict(cs, TaintedAfflictionId, card, Amount);
        }
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (cardPlay.Card.Affliction?.Id != TaintedAfflictionId)
        {
            return;
        }
        PowerCmd.Apply(cs, PowerRegistry.Create(TaintedPowerId), CombatStatus.CardOwnerCreature(cs, cardPlay.Card), Amount, null, null);
    }

    public override void AfterRemoved(CombatState cs, CreatureState oldOwner)
    {
        if (!CombatStatus.IsAttached(cs, oldOwner))
        {
            return;
        }
        var allies = new List<CreatureState>(cs.Allies);
        for (int a = 0; a < allies.Count; a++)
        {
            if (allies[a] is not PlayerState player)
            {
                continue;
            }
            var cards = AllCards(player);
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Affliction?.Id == TaintedAfflictionId)
                {
                    CardCmd.ClearAffliction(cards[i]);
                }
            }
        }
    }

    public override void AfterPowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
        if (!ReferenceEquals(power, this))
        {
            return;
        }
        var allies = new List<CreatureState>(cs.Allies);
        for (int a = 0; a < allies.Count; a++)
        {
            if (allies[a] is not PlayerState player)
            {
                continue;
            }
            var cards = AllCards(player);
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Affliction is { Id: TaintedAfflictionId } affliction)
                {
                    affliction.Amount = Amount;
                }
            }
        }
    }

    private static List<CardInstance> AllCards(PlayerState player)
    {
        var all = new List<CardInstance>();
        var piles = player.AllPiles;
        for (int p = 0; p < piles.Count; p++)
        {
            all.AddRange(piles[p].Cards);
        }
        return all;
    }
}
