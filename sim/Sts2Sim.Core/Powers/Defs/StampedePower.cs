namespace Sts2Sim.Core.Powers;

public sealed class StampedePower : PowerModel
{
    public const string PowerId = "STAMPEDE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterAutoPostPlayPhaseEntered(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return;
        }
        var hand = player.Hand;
        for (int i = 0; i < Amount; i++)
        {
            var items = new List<CardInstance>();
            var cards = hand.Cards;
            for (int j = 0; j < cards.Count; j++)
            {
                var c = cards[j];
                if (c.Type == CardType.Attack && !c.HasKeywordInCombat(cs, CardKeyword.Unplayable))
                {
                    items.Add(c);
                }
            }
            var cardModel = cs.Rng.Shuffle.NextItem((IReadOnlyList<CardInstance>)items);
            if (cardModel != null)
            {
                CardCmd.AutoPlay(cs, cardModel, null);
            }
        }
    }
}
