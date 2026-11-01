namespace Sts2Sim.Core.Powers;

public abstract class TemporaryDexterityPower : PowerModel, ITemporaryPower
{
    public override PowerType Type => IsPositive ? PowerType.Buff : PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public abstract string OriginModelId { get; }

    public string InternallyAppliedPowerId => DexterityPower.PowerId;

    protected virtual bool IsPositive => true;

    private int Sign => IsPositive ? 1 : -1;

    public override void BeforeApplied(CombatState cs, CreatureState target, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
        using var throwing = cs.Choice.PushContext(ChoiceContextKind.Throwing);
        PowerCmd.Apply<DexterityPower>(cs, target, Sign * amount, applier, cardSource, silent: true);
    }

    public override void AfterPowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
        if (amount != Amount && ReferenceEquals(power, this))
        {
            PowerCmd.Apply<DexterityPower>(cs, Owner, Sign * amount, applier, cardSource, silent: true);
        }
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        PowerCmd.Remove(cs, this);
        PowerCmd.Apply<DexterityPower>(cs, Owner, -Sign * Amount, Owner, null);
    }
}
