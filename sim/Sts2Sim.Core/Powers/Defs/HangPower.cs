namespace Sts2Sim.Core.Powers;

public sealed class HangPower : PowerModel
{
    public const string PowerId = "HANG_POWER";

    public const string HangCardId = "HANG";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!ReferenceEquals(target, Owner))
        {
            return 1m;
        }
        if (cardSource == null || !string.Equals(cardSource.Id, HangCardId, StringComparison.Ordinal))
        {
            return 1m;
        }
        return Amount;
    }
}
