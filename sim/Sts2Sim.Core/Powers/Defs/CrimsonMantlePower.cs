namespace Sts2Sim.Core.Powers;

public sealed class CrimsonMantlePower : PowerModel
{
    public const string PowerId = "CRIMSON_MANTLE_POWER";

    public const decimal BaseSelfDamage = 0m;

    public const ValueProp SelfDamageProps = ValueProp.Unblockable | ValueProp.Unpowered;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public decimal SelfDamage { get; private set; } = BaseSelfDamage;

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return;
        }
        CreatureCmd.Damage(cs, Owner, SelfDamage, SelfDamageProps, Owner);
        CreatureCmd.GainBlock(cs, Owner, Amount, ValueProp.Unpowered, null);
    }

    public void IncrementSelfDamage()
    {
        SelfDamage += 1m;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteDecimal(SelfDamage);
    }
}
