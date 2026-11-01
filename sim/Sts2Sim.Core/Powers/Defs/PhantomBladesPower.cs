namespace Sts2Sim.Core.Powers;

public sealed class PhantomBladesPower : PowerModel
{
    public const string PowerId = "PHANTOM_BLADES_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCardEnteredCombat(CombatState cs, CardInstance card)
    {
        if (!card.HasTag(CardTag.Shiv))
        {
            return;
        }
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner as PlayerState))
        {
            return;
        }
        CardCmd.ApplyKeyword(card, CardKeyword.Retain);
    }

    public override void AfterApplied(CombatState cs, CreatureState? applier, CardInstance? cardSource)
    {
        var player = Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} reads its owner's combat cards (Models.Powers/PhantomBladesPower.cs:47), but its owner is not a player");
        var piles = player.AllPiles;
        var shivs = new List<CardInstance>();
        for (int p = 0; p < piles.Count; p++)
        {
            var cards = piles[p].Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].HasTag(CardTag.Shiv))
                {
                    shivs.Add(cards[i]);
                }
            }
        }
        for (int i = 0; i < shivs.Count; i++)
        {
            CardCmd.ApplyKeyword(shivs[i], CardKeyword.Retain);
        }
    }

    public override decimal ModifyDamageAdditive(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!props.IsPoweredAttack())
        {
            return 0m;
        }
        if (cardSource == null || !cardSource.HasTag(CardTag.Shiv))
        {
            return 0m;
        }
        if (!ReferenceEquals(dealer, Owner))
        {
            return 0m;
        }
        var cs = rs.Combat!;
        var owner = Owner;
        int num = cs.History.CountOf<CardPlayFinishedEntry>(e => e.Stamp.HappenedThisTurn(cs) && IsShiv(e.CardPlay.CardId) && owner is PlayerState p && e.CardPlay.PlayerId == p.CombatId);
        if (num > 0)
        {
            return 0m;
        }
        return Amount;
    }

    private static bool IsShiv(string cardId)
        => CardRegistry.TryGet(cardId, out var def) && def.CanonicalTags.Contains(CardTag.Shiv);
}
