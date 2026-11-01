namespace Sts2Sim.Core.Powers;

public sealed class BurrowedPower : PowerModel
{
    public const string PowerId = "BURROWED_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldClearBlock(CombatState cs, CreatureState creature) => !ReferenceEquals(Owner, creature);

    public override void AfterBlockBroken(CombatState cs, CreatureState target, CreatureState? breaker)
    {
        if (!ReferenceEquals(target, Owner))
        {
            return;
        }
        if (target is MonsterState { Def: Tunneler } tunneler)
        {
            Tunneler.GetStunned(cs, tunneler);
            CreatureCmd.StunWithMove(cs, tunneler, Tunneler.StillDizzyMove, Tunneler.BiteMoveId);
        }
        PowerCmd.Remove<BurrowedPower>(cs, Owner);
    }

    public override void AfterRemoved(CombatState cs, CreatureState oldOwner)
    {
        CreatureCmd.LoseBlock(cs, oldOwner, MaxAmount, null);
    }
}
