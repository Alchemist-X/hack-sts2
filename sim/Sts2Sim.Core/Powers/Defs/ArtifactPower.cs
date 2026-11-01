namespace Sts2Sim.Core.Powers;

public sealed class ArtifactPower : PowerModel
{
    public const string PowerId = "ARTIFACT_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => true;

    public override bool TryModifyPowerAmountReceived(CombatState cs, PowerModel canonicalPower, CreatureState target, decimal amount, CreatureState? applier, out decimal modifiedAmount)
    {
        if (!ReferenceEquals(target, Owner))
        {
            modifiedAmount = amount;
            return false;
        }
        if (canonicalPower.GetTypeForAmount(amount) != PowerType.Debuff)
        {
            modifiedAmount = amount;
            return false;
        }
        if (!canonicalPower.IsVisible)
        {
            modifiedAmount = amount;
            return false;
        }
        modifiedAmount = 0m;
        return true;
    }

    public override void AfterModifyingPowerAmountReceived(CombatState cs, PowerModel power)
    {
        PowerCmd.Decrement(cs, this);
    }

    public override decimal GetScaledAmountForMultiplayer(CombatState cs, CreatureState? applier, decimal amount, CreatureState target, CardInstance? cardSource)
        => amount + CombatStatus.PlayerCount(cs) - 1m;
}
