namespace Sts2Sim.Core.Powers;

public sealed class LethalityPower : PowerModel
{
    public const string PowerId = "LETHALITY_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!props.IsPoweredAttack())
        {
            return 1m;
        }
        if (cardSource == null)
        {
            return 1m;
        }
        var cs = rs.Combat!;
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, cardSource), Owner))
        {
            return 1m;
        }
        var pile = cardSource.GetCombatPile(cs);
        if (pile != null && pile.Type == PileType.Play && cardSource.CurrentPlayIndex > 0)
        {
            return 1m;
        }
        var owner = Owner;
        int num = cs.History.CountOf<CardPlayStartedEntry>(e => e.Stamp.HappenedThisTurn(cs) && e.CardPlay.CardType == CardType.Attack && owner is PlayerState p && e.CardPlay.PlayerId == p.CombatId);
        int num2 = pile != null && pile.Type == PileType.Play ? 1 : 0;
        if (num > num2)
        {
            return 1m;
        }
        return 1m + (decimal)Amount / 100m;
    }
}
