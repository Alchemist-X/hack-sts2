namespace Sts2Sim.Core.Powers;

public sealed class HeistPower : PowerModel
{
    public const string PowerId = "HEIST_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public override void BeforeDeath(RunState rs, CreatureState creature)
    {
        if (!ReferenceEquals(Owner, creature))
        {
            return;
        }
        var cs = rs.Combat!;
        var player = GetTarget(cs) as PlayerState ?? throw new InvalidOperationException("HEIST_POWER.BeforeDeath: Target.Player is null");
        if (RoomStack.CurrentRoom(rs) is CombatRoom combatRoom)
        {
            combatRoom.AddExtraReward(new GoldReward(Amount, player, wasGoldStolenBack: true));
        }
    }
}
