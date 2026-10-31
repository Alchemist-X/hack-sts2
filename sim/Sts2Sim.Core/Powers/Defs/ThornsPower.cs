namespace Sts2Sim.Core.Powers;

public sealed class ThornsPower : PowerModel
{
    public const string PowerId = "THORNS_POWER";

    public const string OmnisliceCardId = "OMNISLICE";

    public const ValueProp RetaliationProps = ValueProp.Unpowered | ValueProp.SkipHurtAnim;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeDamageReceived(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (ReferenceEquals(target, Owner) && dealer != null && (props.IsPoweredAttack() || IsOmnislice(cardSource)))
        {
            CreatureCmd.Damage(cs, dealer, Amount, RetaliationProps, Owner, null, null);
        }
    }

    private static bool IsOmnislice(CardInstance? cardSource)
        => cardSource != null && string.Equals(cardSource.Id, OmnisliceCardId, StringComparison.Ordinal);
}
