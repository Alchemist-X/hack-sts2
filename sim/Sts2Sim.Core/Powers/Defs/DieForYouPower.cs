namespace Sts2Sim.Core.Powers;

public sealed class DieForYouPower : PowerModel
{
    public const string PowerId = "DIE_FOR_YOU_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override CreatureState ModifyUnblockedDamageTarget(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer)
    {
        var owner = Owner;
        if (!ReferenceEquals(target, CombatStatus.PetOwnerOf(cs, owner)))
        {
            return target;
        }
        if (owner.IsDead)
        {
            return target;
        }
        if (!props.IsPoweredAttack())
        {
            return target;
        }
        return owner;
    }

    public override bool ShouldAllowHitting(CombatState cs, CreatureState creature) => creature.IsAlive;

    public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState cs, CreatureState creature) => !ReferenceEquals(creature, Owner);

    public override bool ShouldPowerBeRemovedAfterOwnerDeath() => false;
}
