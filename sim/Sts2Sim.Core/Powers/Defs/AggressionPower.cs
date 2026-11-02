namespace Sts2Sim.Core.Powers;

public sealed class AggressionPower : PowerModel
{
    public const string PowerId = "AGGRESSION_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        var player = OwnerPlayer();
        var attacks = new List<CardInstance>();
        var discard = player.DiscardPile.Cards;
        for (int i = 0; i < discard.Count; i++)
        {
            if (discard[i].Type == CardType.Attack)
            {
                attacks.Add(discard[i]);
            }
        }
        var shuffled = attacks.UnstableShuffle(cs.Rng.CombatCardSelection);
        int take = Math.Min(Amount, shuffled.Count);
        for (int i = 0; i < take; i++)
        {
            var card = shuffled[i];
            CardPileCmd.Add(cs, card, PileType.Hand);
            if (card.IsUpgradable)
            {
                CardCmd.Upgrade(cs, card);
            }
        }
    }

    private PlayerState OwnerPlayer()
        => Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} reads its owner's discard pile (Models.Powers/AggressionPower.cs:58), but its owner is not a player");
}
