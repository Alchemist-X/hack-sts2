namespace Sts2Sim.Core.Powers;

public sealed class BurrowedPower : PowerModel
{
    public const string PowerId = "BURROWED_POWER";

    public const string TunnelerId = "TUNNELER";

    public const string StunNextMoveId = "BITE_MOVE";

    public const decimal LoseAllBlock = 999999999m;

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
        if (!MonsterPowerCoupling.Is(target, TunnelerId, out var tunneler))
        {
            return;
        }
        MonsterPowerCoupling.Call(cs, tunneler, "GetStunned", PowerId);
        CreatureCmd.StunWithMove(cs, tunneler, MonsterPowerCoupling.Move(tunneler, "StillDizzyMove", PowerId), StunNextMoveId);
        PowerCmd.Remove<BurrowedPower>(cs, Owner);
    }

    public override void AfterRemoved(CombatState cs, CreatureState oldOwner)
    {
        CreatureCmd.LoseBlock(cs, oldOwner, LoseAllBlock, null);
    }
}
