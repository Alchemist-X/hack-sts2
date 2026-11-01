namespace Sts2Sim.Core.Powers;

public sealed class GalvanicPower : PowerModel
{
    public const string PowerId = "GALVANIC_POWER";

    public const string GalvanizedAfflictionId = "GALVANIZED";

    public const ValueProp DamageProps = ValueProp.Unpowered | ValueProp.Move;

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
            var powers = new List<CardInstance>();
            var piles = player.AllPiles;
            for (int p = 0; p < piles.Count; p++)
            {
                var cards = piles[p].Cards;
                for (int i = 0; i < cards.Count; i++)
                {
                    if (cards[i].Type == CardType.Power)
                    {
                        powers.Add(cards[i]);
                    }
                }
            }
            for (int i = 0; i < powers.Count; i++)
            {
                CardCmd.Afflict(cs, GalvanizedAfflictionId, powers[i], Amount);
            }
        }
    }

    public override void AfterCardEnteredCombat(CombatState cs, CardInstance card)
    {
        if (card.Affliction == null && card.Type == CardType.Power)
        {
            CardCmd.Afflict(cs, GalvanizedAfflictionId, card, Amount);
        }
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (cardPlay.Card.Affliction?.Id != GalvanizedAfflictionId)
        {
            return;
        }
        CreatureCmd.Damage(cs, CombatStatus.CardOwnerCreature(cs, cardPlay.Card), Amount, DamageProps, null, null, null);
    }
}
