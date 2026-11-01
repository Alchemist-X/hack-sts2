namespace Sts2Sim.Core.Powers;

public sealed class CalcifyPower : PowerModel
{
    public const string PowerId = "CALCIFY_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageAdditive(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (dealer is not MonsterState monster || !string.Equals(monster.MonsterId, CombatStatus.OstyMonsterId, StringComparison.Ordinal))
        {
            return 0m;
        }
        if (monster.PetOwnerId == NoCreature || Owner.CombatId != monster.PetOwnerId)
        {
            return 0m;
        }
        if (!props.IsPoweredAttack())
        {
            return 0m;
        }
        return Amount;
    }
}
