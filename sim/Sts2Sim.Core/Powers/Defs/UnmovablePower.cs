namespace Sts2Sim.Core.Powers;

public sealed class UnmovablePower : PowerModel
{
    public const string PowerId = "UNMOVABLE_POWER";

    public const decimal BlockMultiplier = 2m;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyBlockMultiplicative(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (target.IsMonster)
        {
            return 1m;
        }
        if (!props.IsCardOrMonsterMove())
        {
            return 1m;
        }
        if (cardSource != null && !ReferenceEquals(CombatStatus.CardOwnerCreature(cs, cardSource), Owner))
        {
            return 1m;
        }
        int ownerId = Owner.CombatId;
        int num = cs.History.CountOf<BlockGainedEntry>(e => e.Stamp.HappenedThisTurn(cs) && e.CardPlay is { } play && play.PlayerId == ownerId && e.Props.IsCardOrMonsterMove() && (cardPlay == null || play.PlayId != cardPlay.PlayId));
        if (num >= Amount)
        {
            return 1m;
        }
        return BlockMultiplier;
    }
}
